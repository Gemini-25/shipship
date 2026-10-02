using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

// v16.26 ① ④ 계획의 걸음들 · 수순 짜기 · 가벼운 모형.
//  냉각 펌프 고장: 고장 펌프만 고친다 / 출력을 낮추고 고친다 / 예비 펌프로 버티며 고친다 / 원자로를 최저로 두고 고친다 — 부품이 없으면 "부품 구하기"가 앞에 선다.
//  닫으라 했는데 열린 문: 한 겹 뒤 문 · 손으로 닫기 · 다시 열기.  핵심 설비 고장: 가장 잘하는 사람 / 지금 비어 있는 사람 — 수리 → 시험 운전 → 재고장 감시.
//  모형은 컴퓨터가 믿는 값으로 돈다(배터리 실제 용량 · 사람별 작업 시간은 ⑦ 검토가 고쳐 준다) — 그래서 가끔 틀린다.

public static class FixSteps
{
    /// <summary>안전 제한: 노심 최고 온도는 긴급 정지보다 이만큼은 낮게 (배우지 않는다).</summary>
    public const float SafetyFloorC = 12f;

    public static float ScramC(World w)
    {
        int rpol = w.Policies["reactor"];
        return rpol == 0 ? PowerGrid.OverheatScramC - 15f : rpol == 2 ? PowerGrid.OverheatScramC + 25f : PowerGrid.OverheatScramC;
    }

    private static float FaultFactor(Machine m) => m.Faults.Count == 0 ? 1f : m.Faults.Min(f => f.Kind == FaultKind.BreakerTrip ? 0f : f.OutputFactor);
    internal static bool Broken(Machine m) => m.Faults.Any(f => f.Kind != FaultKind.BreakerTrip && f.OutputFactor < 0.9f);

    // ───────────── 냉각 ─────────────

    internal static FixCase CoolingCase(World w, Furniture pump)
    {
        var p = w.Power;
        var a = w.Automation;
        var m = pump.Machine!;
        var c = new FixCase { Problem = "냉각", Target = pump, Room = pump.Room, T0 = p.ReactorTemperature, Scram = ScramC(w), Warn = PowerGrid.OverheatWarnC, ReactorOn = p.ReactorOnline };
        c.Cool = MathF.Max(0.5f, p.EffectiveCooling);
        c.LostKw = PipeNetwork.PerBranchKw * m.Rating * (1f - FaultFactor(m)) * 0.9f;
        float others = 0f;
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.CoolantPump))
            if (f != pump && f.Machine is Machine om && !Broken(om) && !FixBook.WorkLock(w, om)) others += PipeNetwork.PerBranchKw * om.Rating * om.Efficiency * 0.9f;
        c.Boosted = a.Recovery.Boosting;
        c.BoostKw = c.Boosted ? 0f : others * 0.25f;
        float capNow = a.ReactorCap * (p.MaintenanceCap ? 0.5f : 1f);
        c.Cap = capNow;
        c.RMax = MathF.Max(1f, p.ReactorLimit / MathF.Max(0.1f, capNow));
        c.Demand = MathF.Min(c.RMax, MathF.Max(p.ReactorOutput, p.Demand));
        (c.BatteryKwh, c.BatteryCap) = a.Review.BatteryBelief();
        var fault = m.Faults.Where(f => f.Kind != FaultKind.BreakerTrip).OrderBy(f => f.OutputFactor).FirstOrDefault();
        c.RepairHours = fault != null ? fault.Spec.RepairHours * (1f - 0.25f * fault.Stage) : 0.5f;
        c.HavePart = fault == null || fault.Materials.All(x => w.Ship.CountStored(x.kind) >= x.count);
        c.PartName = fault?.Part is ItemKind part ? ItemKinds.Name(part) : "";
        c.Hand = Hand(w, m, null);
        c.Pace = c.Hand != null ? a.Review.Values.PaceOf(c.Hand.Id) : 1.2f;
        return c;
    }

    /// <summary>냉각 펌프 고장 → 수순 넷을 견준다 (replacing이면 지금 상태에서 다시).</summary>
    internal static FixPlan MakeCooling(World w, FixBook book, Furniture pump, FixPlan? replacing = null, string why = "", string prefer = "")
    {
        var c = CoolingCase(w, pump);
        var m = pump.Machine!;
        int id = pump.Id;
        float cap1 = Math.Clamp(MathF.Floor(c.Cool * 0.92f / c.RMax * 10f) / 10f, 0.3f, 0.9f);
        float cap2 = Math.Clamp(MathF.Floor((c.Cool + c.BoostKw) * 0.92f / c.RMax * 10f) / 10f, 0.4f, 0.95f);
        bool broken = Broken(m);
        bool done = replacing != null && !broken; // 이미 고쳐졌다 — 시험 운전부터
        FixAction[] Fix() => done ? new FixAction[] { new TestRunStep(id) } : c.HavePart ? new FixAction[] { new RepairStep(id), new TestRunStep(id) } : new FixAction[] { new PartStep(id), new RepairStep(id), new TestRunStep(id) };
        var opts = new List<FixOption>();
        FixOption Opt(string key, string name, params FixAction[][] parts)
        {
            var o = new FixOption { Key = key, Name = name };
            foreach (var part in parts) o.Steps.AddRange(part);
            opts.Add(o);
            return o;
        }
        Opt("fix", "고장 펌프만 고친다", Fix(), new FixAction[] { new WatchStep(id) });
        var der = Opt("derate", $"출력을 {cap1 * 100:0}%로 낮추고 고친다", new FixAction[] { new DerateStep(cap1) }, Fix(), new FixAction[] { new RampStep(), new WatchStep(id) });
        var bst = Opt("boost", "예비 펌프로 버티며 고친다", c.Boosted ? Array.Empty<FixAction>() : new FixAction[] { new BoostStep(id) }, new FixAction[] { new DerateStep(cap2) }, Fix(), new FixAction[] { new RampStep(), new WatchStep(id) });
        var low = Opt("low", "원자로를 최저로 두고 고친다", new FixAction[] { new DerateStep(0.15f) }, Fix(), new FixAction[] { new RampStep(), new WatchStep(id) });
        if (!c.Boosted && c.BoostKw < 1f) { bst.Allowed = false; bst.Blocked = "같이 돌릴 펌프가 없다"; }
        if (cap1 >= 0.9f && c.Demand <= c.Cool) { der.Allowed = false; der.Blocked = "낮출 까닭이 없다 (남은 냉각으로 충분)"; }
        if (!c.ReactorOn) foreach (var o in opts.Where(o => o.Key != "fix")) { o.Allowed = false; o.Blocked = "원자로가 꺼져 있다"; }
        if (prefer != "" && opts.FirstOrDefault(o => o.Key == prefer) is FixOption pf && pf.Allowed) foreach (var o in opts) if (o != pf) o.Score += 1000f; // 중단 조건이 정한 대안
        foreach (var o in opts)
        {
            Model(w, c, o);
            o.Success = $"냉각 {c.Cool + c.LostKw:0}kW로 돌아오고 노심 {c.Warn - 30f:0}℃ 아래로 20분 버틴다";
            o.Abort = $"노심 {c.Scram - 10f:0}℃ · 시험 운전 유량 75% 아래 · 배터리 15% 아래";
            o.Needs = (c.HavePart ? $"{c.PartName} 1개(있음)" : $"{c.PartName} 1개(없음 — 구해야 한다)") + $" · 정비 {(c.Hand != null ? c.Hand.Name : "누군가")} 1명" + (o.BatteryKwh > 0.5f ? $" · 배터리 {o.BatteryKwh:0}kWh" : "");
            o.After = o.Key switch
            {
                "boost" => "다른 펌프가 더 빨리 닳는다 · 출력이 줄어 배터리를 쓴다",
                "derate" => "출력이 줄어 배터리를 쓴다 · 길어지면 생활 설비를 내린다",
                "low" => "배터리를 크게 쓴다 · 다시 올리는 데 시간이 든다",
                _ => "노심이 달아오를 수 있다 — 긴급 정지면 몇 시간 못 켠다",
            };
        }
        string goal = $"{pump.Name} 고장 — 냉각을 되살린다";
        return book.Choose("냉각", $"냉각:{id}", pump.Room, id, goal, c, opts, replacing, why);
    }

    /// <summary>가벼운 모형: 분 단위로 노심 온도 · 배터리 · 못 낸 전기를 굴린다 (빠른 쪽 · 느린 쪽 범위).</summary>
    internal static void Model(World w, FixCase c, FixOption o)
    {
        var (fMin, fPeak, fBat, fLost, fScram) = Sim(w, c, o, false);
        var (sMin, sPeak, sBat, sLost, sScram) = Sim(w, c, o, true);
        o.Min = fMin; o.Max = sMin; o.PeakMin = fPeak; o.PeakMax = sPeak; o.BatteryKwh = sBat; o.LostKwh = sLost;
        float limit = c.Scram - SafetyFloorC;
        o.Risk = sScram ? (fScram ? 0.95f : 0.6f) : sPeak > limit ? Math.Clamp(0.3f * (sPeak - limit) / SafetyFloorC, 0.05f, 0.5f) : 0f;
        if (c.Problem == "냉각" && c.BatteryCap > 0f && sBat > c.BatteryKwh * 0.85f) { o.Risk = MathF.Max(o.Risk, 0.4f); o.Note = "배터리가 수리 동안 못 버틸 수 있다"; }
    }

    private static (float minutes, float peak, float battery, float lost, bool scram) Sim(World w, FixCase c, FixOption o, bool slow)
    {
        float T = c.T0, cool = c.Cool, cap = c.Cap, batt = c.BatteryKwh, used = 0f, lost = 0f, peak = T, minutes = 0f, boost = 0f;
        bool scram = false;
        const float k = 1f - 0.0487f; // exp(-3/60)
        void Minute()
        {
            float h = c.ReactorOn ? MathF.Min(c.Demand, c.RMax * cap) : 0f;
            float cc = cool + boost;
            if (h > cc) T += (h - cc) / PowerGrid.CoreHeatKwhPerC / 60f;
            else { float eq = 200f + 140f * Math.Clamp(h / MathF.Max(0.1f, cc), 0f, 1f); T = eq + (T - eq) * k; }
            float shortKw = c.Demand - h;
            if (shortKw > 0f) { if (batt > 0f) { batt -= shortKw / 60f; used += shortKw / 60f; } else lost += shortKw / 60f; }
            if (T > peak) peak = T;
            if (T >= c.Scram && c.ReactorOn) { scram = true; cap = 0f; }
        }
        foreach (var act in o.Steps)
        {
            var (mn, mx) = act.Span(w, c);
            int dur = (int)MathF.Ceiling(slow ? mx : mn);
            float from = cap;
            boost += act.CoolAdd(c);
            if (act is RampStep)
            {
                for (int i = 0; i < dur && !scram; i++) { cap = from + (1f - from) * (i + 1) / dur; Minute(); }
                boost = 0f;
            }
            else
            {
                if (act.CapSet >= 0f && !scram) cap = act.CapSet;
                for (int i = 0; i < dur; i++) Minute();
            }
            if (act.Restores) cool += c.LostKw;
            minutes += dur;
            if (dur > 600) break;
        }
        for (int i = 0; i < 10; i++) Minute();
        return (minutes, peak, used, lost, scram);
    }

    /// <summary>점수: 위험 · 시간 · 배터리 · 못 낸 전기(생활 불편) · 불확실 · 지난 경험 — 성격 셋째 층(판단 선호)이 무게를 정한다.</summary>
    internal static void Score(World w, FixCase c, FixOption o)
    {
        var a = w.Automation;
        var mn = a.Manner;
        float margin = SafetyFloorC + 10f * mn.MarginPref; // 여유를 얼마나 남길지 (안전 제한 아래로는 안 내려간다)
        float over = c.Scram > 0f ? MathF.Max(0f, o.PeakMax - (c.Scram - margin)) : 0f;
        float comfort = 0.5f + 0.5f * (1f - mn.ComfortPref); // 생활 불편을 덜 감수하면 못 낸 전기 · 배터리를 더 무겁게
        o.Score += o.Risk * 100f + over * 1.5f + (o.Min + o.Max) * 0.5f * 0.03f + o.LostKwh * 0.8f * comfort
                   + (c.BatteryCap > 0f ? o.BatteryKwh / c.BatteryCap * 20f * comfort : 0f)
                   + MathF.Max(0f, a.Character.Caution) * (o.Max - o.Min) / 30f
                   + a.Review.Prefer(c.Problem, o.Key);
        if (!o.Allowed) o.Score += 500f;
    }

    internal static string Why(FixOption pick, FixOption second)
    {
        if (second.Risk > pick.Risk + 0.1f) return second.PeakMax > 0f ? $"{second.Name}이면 노심이 {second.PeakMax:0}℃까지 오를 수 있다 (이쪽 {pick.PeakMax:0}℃)" : $"{second.Name}보다 덜 위험하다";
        if (second.BatteryKwh > pick.BatteryKwh + 3f) return $"배터리를 덜 쓴다 ({pick.BatteryKwh:0} · {second.Name}이면 {second.BatteryKwh:0}kWh)";
        if (second.LostKwh > pick.LostKwh + 1f) return $"생활 설비를 덜 끈다 ({second.Name}보다)";
        if (second.Max > pick.Max + 5f) return $"더 빨리 끝난다 ({pick.Range()} · {second.Name}이면 {second.Range()})";
        return $"견줘 보니 조금 낫다 ({second.Name}보다)";
    }

    private static string Range(this FixOption o) => o.Max - o.Min < 1.5f ? $"{o.Min:0}분" : $"{o.Min:0}~{o.Max:0}분";

    /// <summary>손 하나 고르기: 기술 · 기력 · 이 배에서 잰 작업 속도 · 다른 계획에 이미 잡힌 사람은 피한다 (전문가 몰림 분산).</summary>
    /// <summary>그 사람이 다른 열린 계획의 사람 손 걸음을 하고 있나 (전문가에게 몰림).</summary>
    internal static bool OnOtherPlan(World w, int crewId, FixPlan p)
    {
        foreach (var q in w.Automation.Recovery.Plans)
            if (q != p && q.Open) foreach (var s in q.Steps) if (s.State == FixState.Run && s.Act.Kind == FixKind.Hands && s.Crew == crewId) return true;
        return false;
    }

    internal static CrewMember? Hand(World w, Machine m, HashSet<int>? skip, bool simple = false)
    {
        var a = w.Automation;
        var busy = new HashSet<int>();
        foreach (var p in a.Recovery.Plans) if (p.Open) foreach (var s in p.Steps) if (s.State == FixState.Run && s.Act.Kind == FixKind.Hands && s.Crew >= 0) busy.Add(s.Crew);
        CrewMember? best = null;
        float bs = float.MinValue;
        foreach (var c in w.Crew)
        {
            if (c.Dead || c.IsChild || !c.CanAct || c.Outside || c.Away || skip?.Contains(c.Id) == true) continue;
            float rest = c.Needs.Rest;
            if (rest < 0.15f) continue;
            float skill = c.SkillLevel(m.Spec.Skill);
            float pace = a.Review.Values.PaceOf(c.Id);
            float sc = skill * (simple ? 0.4f : 1f) + 0.3f * rest - 0.25f * (pace - 1f) - (busy.Contains(c.Id) ? 0.6f : 0f) - (c.Pose == Pose.Sleeping ? 0.3f : 0f)
                       - (c.Room != m.Body.Room ? 0.05f : 0f);
            if (sc > bs || sc == bs && best != null && c.Id < best.Id) { bs = sc; best = c; }
        }
        if (best != null && busy.Contains(best.Id)) a.Recovery.Spread++;
        return best;
    }

    // ───────────── 문 ─────────────

    /// <summary>닫으라 했는데 이 배에서 잰 닫히는 시간을 넘겨도 열려 있다.</summary>
    internal static bool DoorStuck(World w, Door d)
    {
        var book = w.Automation.Recovery;
        if (!book.DoorSeen.TryGetValue(d.Id, out long seen)) return false;
        float sec = w.Automation.Review.Values.DoorCloseSec;
        return (w.Tick - seen) * 3600f / SimTime.TicksPerHour > sec * 1.5f + 20f;
    }

    internal static FixPlan MakeDoor(World w, FixBook book, Door d, Room low, FixPlan? replacing = null, string why = "")
    {
        var high = d.RoomA == low ? d.RoomB : d.RoomA;
        string cause = d.JammedOpen ? "열에 휘어 걸렸다" : d.Blocked ? "잔해가 끼었다" : d.Bent > 0.3f ? "문틀이 휘었다" : d.MotorBroken ? "구동기가 안 돈다" : !d.Powered ? "전기가 없다" : "까닭을 모른다";
        var c = new FixCase { Problem = "문", Door = d, Room = low };
        var opts = new List<FixOption>();
        var detour = new FixOption { Key = "detour", Name = "한 겹 뒤 문을 닫고 손으로 닫는다" };
        detour.Steps.Add(new DoorBackStep(d.Id, low.Id));
        detour.Steps.Add(new CrankStep(d.Id));
        detour.Steps.Add(new DoorReleaseStep(d.Id, low.Id));
        var crank = new FixOption { Key = "crank", Name = "손으로 닫는다" };
        crank.Steps.Add(new CrankStep(d.Id));
        opts.Add(detour); opts.Add(crank);
        bool canBack = high != null && !high.Detached && high.Type != RoomType.Corridor && w.Automation.DoorsIn(high);
        if (!canBack) { detour.Allowed = false; detour.Blocked = high == null ? "뒤에 방이 없다" : high.Type == RoomType.Corridor ? "뒤가 통로 — 닫으면 길이 끊긴다" : "뒤 방 문을 원격으로 못 움직인다"; }
        foreach (var o in opts)
        {
            o.Min = o.Key == "detour" ? 6f : 8f; o.Max = o.Key == "detour" ? 25f : 30f;
            // 한 겹 뒤를 닫으면 사람이 오기 전까지 새는 공기가 그 방에 갇힌다
            o.Risk = o.Key == "crank" ? 0.25f : 0.05f;
            o.Success = "문 센서가 닫힘을 읽고 기압 차가 버틴다";
            o.Abort = "뒤 방에 사람이 갇힌다 · 손으로도 안 닫힌다";
            o.Needs = o.Key == "detour" ? "뒤 방이 비어야 한다 · 지렛대 든 사람 1명" : "지렛대 든 사람 1명";
            o.After = o.Key == "detour" ? $"{high?.Name ?? "뒤 방"} 길이 잠시 막힌다" : "사람이 올 때까지 공기가 샌다";
        }
        var p = book.Choose("문", $"문:{d.Id}", low, d.Id, $"{low.Name} 격벽 — 닫으라 했는데 열려 있다", c, opts, replacing, why);
        if (replacing == null)
        {
            // 첫 두 걸음은 이미 지나갔다: 명령은 들어갔고 · 센서는 열림
            p.Steps.Insert(0, new FixStep { Act = new NoteStep("격벽 닫기 명령", FixKind.Remote), State = FixState.Done, Start = p.Tick, End = p.Tick, Note = "명령은 들어갔다" });
            p.Steps.Insert(1, new FixStep { Act = new NoteStep("닫힘 확인", FixKind.Test), State = FixState.Failed, Start = p.Tick, End = p.Tick, Note = $"문 센서가 열림 {d.Openness * 100:0}% — {cause}" });
            p.Cur = 2;
            book.Revise(p, $"닫으라 했는데 문 센서가 열림 ({cause}) — 명령 성공을 완료로 치지 않는다 · {p.Name}");
            book.DoorDetours++;
        }
        return p;
    }

    // ───────────── 핵심 설비 ─────────────

    internal static FixPlan MakeMachine(World w, FixBook book, Machine m, FixPlan? replacing = null, string why = "")
    {
        var a = w.Automation;
        int id = m.Body.Id;
        var fault = m.Faults.Where(f => f.Kind != FaultKind.BreakerTrip).OrderBy(f => f.OutputFactor).FirstOrDefault();
        float hours = fault != null ? fault.Spec.RepairHours * (1f - 0.25f * fault.Stage) : 0.5f;
        bool havePart = fault == null || fault.Materials.All(x => w.Ship.CountStored(x.kind) >= x.count);
        var best = Hand(w, m, null);
        var near = Hand(w, m, best != null ? new HashSet<int> { best.Id } : null, simple: true);
        var c = new FixCase { Problem = "설비", Target = m.Body, Room = m.Body.Room, RepairHours = hours, HavePart = havePart, Hand = best, Pace = best != null ? a.Review.Values.PaceOf(best.Id) : 1.2f };
        var opts = new List<FixOption>();
        bool fixedAlready = replacing != null && !Broken(m);
        foreach (var (key, name, who) in new[] { ("best", "가장 잘하는 사람이 고친다", best), ("near", "비어 있는 사람이 고친다", near) })
        {
            var o = new FixOption { Key = key, Name = name };
            if (!fixedAlready)
            {
                if (!havePart) o.Steps.Add(new PartStep(id));
                o.Steps.Add(new RepairStep(id, prefer: who?.Id ?? -1));
            }
            o.Steps.Add(new TestRunStep(id));
            o.Steps.Add(new WatchStep(id));
            float pace = who != null ? a.Review.Values.PaceOf(who.Id) : 1.3f;
            float skill = who?.SkillLevel(m.Spec.Skill) ?? 0.3f;
            float work = hours * 60f * (1.15f - 0.4f * skill) * pace;
            o.Min = work * 0.8f + 5f + (havePart ? 0f : 40f); o.Max = work * 1.3f + 20f + (havePart ? 0f : 120f);
            o.Risk = MathF.Max(0f, 0.45f - skill) * 0.4f; // 손이 서툴면 다시 해야 할 수 있다
            if (who == null) { o.Allowed = false; o.Blocked = "맡을 사람이 없다"; }
            o.Success = $"{Ko.IGa(m.Name)} 다시 돌고 20분 버틴다";
            o.Abort = "방이 위험해진다 · 부품이 없다";
            o.Needs = (havePart ? "" : "부품 · ") + $"{(who != null ? who.Name : "누군가")} {m.Spec.Skill switch { Skill.Electrical => "전기", Skill.Mechanics => "기계", _ => Skills.Name(m.Spec.Skill) }} 손";
            o.After = key == "best" ? "그 사람이 하던 일이 밀린다" : "더 오래 걸릴 수 있다";
            opts.Add(o);
        }
        return book.Choose("설비", $"설비:{id}", m.Body.Room, id, $"{m.Name} 고장 — 다시 돌린다", c, opts, replacing, why);
    }

    internal static void Cleanup(World w, FixPlan p)
    {
        foreach (var s in p.Steps) if (s.Act is DoorBackStep b) b.Release(w);
    }
}

// ───────────────────────── 걸음들 ─────────────────────────

/// <summary>이미 지나간 걸음 (기록용).</summary>
internal sealed class NoteStep : FixAction
{
    private readonly string _name;
    private readonly FixKind _kind;
    public NoteStep(string name, FixKind kind) { _name = name; _kind = kind; }
    public override string Name => _name;
    public override FixKind Kind => _kind;
    public override FixState Tick(World w, FixPlan p, FixStep s) => FixState.Done;
}

/// <summary>예비 펌프로 시간 벌기: 다른 펌프를 더 세게 돌린다 (마모가 빨라진다).</summary>
internal sealed class BoostStep : FixAction
{
    private readonly int _failed;
    public BoostStep(int failed) => _failed = failed;
    public override string Name => "예비 펌프로 시간 벌기";
    public override float CoolAdd(FixCase c) => c.BoostKw;
    public override (float min, float max) Span(World w, FixCase c) => (1f, 2f);
    public override void Begin(World w, FixPlan p, FixStep s)
    {
        var a = w.Automation;
        s.Mark = (long)(w.Power.EffectiveCooling * 100f);
        if (s.Issued) return; // 예비 연산기로 넘어가도 두 번 보내지 않는다
        s.Issued = true;
        s.IssuedAt = w.Tick;
        foreach (var f in w.Ship.FurnitureOf(FurnitureType.CoolantPump))
        {
            if (f.Id == _failed || f.Machine is not Machine m || FixSteps.Broken(m) || FixBook.WorkLock(w, m)) continue;
            a.Recovery.SetDrive(f, 1.25f);
            a.Command.Line(CmdTarget.Machine, f.Id, f.Room, $"{f.Name} 세기 125%", "고장 난 펌프 몫을 나눠 진다 — 그만큼 빨리 닳는다", 0.8f, 30f, state: "하는 중");
            a.SelfWatch.Note(f.Id, "펌프 세기");
        }
    }
    public override FixState Tick(World w, FixPlan p, FixStep s)
    {
        float gain = w.Power.EffectiveCooling - s.Mark / 100f;
        if (s.Took(w.Tick) < 1f) return FixState.Run;
        if (gain > 0.3f || w.Power.EffectiveCooling >= w.Power.ReactorOutput) { s.Note = $"냉각 +{MathF.Max(0f, gain):0.#}kW"; return FixState.Done; }
        if (s.Took(w.Tick) >= 3f) { s.Note = "다른 펌프가 따라오지 않는다"; return FixState.Failed; }
        return FixState.Run;
    }
    public override void Cancel(World w, FixPlan p, FixStep s) { }
}

/// <summary>원자로 출력 낮추기 (감출력).</summary>
internal sealed class DerateStep : FixAction
{
    private readonly float _cap;
    public DerateStep(float cap) => _cap = cap;
    public override string Name => _cap <= 0.2f ? "원자로 출력 최저로" : $"출력 {_cap * 100:0}%로 낮추기";
    public override float CapSet => _cap;
    public override (float min, float max) Span(World w, FixCase c) => (2f, 3f);
    public override void Begin(World w, FixPlan p, FixStep s)
    {
        var a = w.Automation;
        a.Recovery.ReactorCap = MathF.Min(a.Recovery.ReactorCap, _cap);
        if (s.Issued) return;
        s.Issued = true;
        s.IssuedAt = w.Tick;
        a.Command.Line(CmdTarget.Reactor, w.Power.Reactor?.Body.Id ?? -1, w.Power.Reactor?.Body.Room, $"원자로 출력 {_cap * 100:0}%로", $"냉각이 모자라는 동안 열을 덜 낸다 — 노심 {w.Power.ReactorTemperature:0}℃", 0.85f, 30f, state: "하는 중");
        if (w.Power.MaintenanceCap) s.Note = "정비 절차로 이미 절반 — 그대로 둔다";
    }
    public override FixState Tick(World w, FixPlan p, FixStep s)
    {
        var a = w.Automation;
        a.Recovery.ReactorCap = MathF.Min(a.Recovery.ReactorCap, _cap);
        if (!w.Power.ReactorOnline || s.Took(w.Tick) >= 2f) { if (s.Note == "") s.Note = $"출력 {w.Power.ReactorOutput:0.#}kW"; return FixState.Done; }
        return FixState.Run;
    }
}

/// <summary>부품 구하기: 오기 전엔 사람을 세워 두지 않는다.</summary>
internal sealed class PartStep : FixAction
{
    private readonly int _furn;
    public PartStep(int furn) => _furn = furn;
    public override string Name => "부품 구하기";
    public override FixKind Kind => FixKind.Wait;
    public override (float min, float max) Span(World w, FixCase c) => (30f, 150f); // 만드는 데 얼마나 걸릴지 모른다
    private static (ItemKind kind, int count)[] Need(World w, int id) =>
        w.Ship.Furniture.FirstOrDefault(f => f.Id == id)?.Machine?.Faults.Where(f => f.Kind != FaultKind.BreakerTrip).SelectMany(f => f.Materials).ToArray() ?? Array.Empty<(ItemKind, int)>();
    public override FixState Tick(World w, FixPlan p, FixStep s)
    {
        var need = Need(w, _furn);
        var missing = need.Where(x => w.Ship.CountStored(x.kind) < x.count).Select(x => ItemKinds.Name(x.kind)).ToList();
        if (missing.Count == 0) { s.Note = "부품이 생겼다"; return FixState.Done; }
        s.Waiting = $"{Ko.IGa(string.Join("·", missing))} 오기를 기다림 — 사람은 아직 세우지 않는다";
        return FixState.Run;
    }
    public override string? Late(World w, FixPlan p, FixStep s) => "부품이 예상보다 늦다 — 감출력을 그대로 두고 기다린다";
}

/// <summary>사람 손 수리: 고를 사람 → 부탁 → 늦으면 다른 사람 · 현장에서 안 된다 하면 대안.</summary>
internal sealed class RepairStep : FixAction
{
    private readonly int _furn;
    private readonly int _prefer;
    private readonly HashSet<int> _late = new();
    public RepairStep(int furn, int prefer = -1) { _furn = furn; _prefer = prefer; }
    public override int Target => _furn;
    private Machine? M(World w) => w.Ship.Furniture.FirstOrDefault(f => f.Id == _furn)?.Machine;
    public override string Name => "수리";
    public override FixKind Kind => FixKind.Hands;
    public override bool Restores => true;
    public override (float min, float max) Span(World w, FixCase c)
    {
        float skill = c.Hand?.SkillLevel(c.Target?.Machine?.Spec.Skill ?? Skill.Mechanics) ?? 0.3f;
        float work = c.RepairHours * 60f * (1.15f - 0.4f * skill) * c.Pace;
        return (work * 0.8f + 6f, work * 1.3f + 20f + (c.Hand == null ? 20f : 0f));
    }
    private static WorkOrder? Order(World w, Machine m) => w.Board.All.FirstOrDefault(o => !o.Closed && o.Kind == WorkKind.Repair && o.Target.Furniture == m.Body);
    public override string? Ready(World w, FixPlan p, FixStep s)
    {
        var m = M(w);
        if (m == null || !FixSteps.Broken(m)) return null;
        var room = m.Body.Room;
        if (room.Leaking || w.Fire.IsKnown(room) || room.Air.O2 < 15f) return "방이 위험하다 — 사람을 들여보내지 않는다";
        var miss = m.Faults.Where(f => f.Kind != FaultKind.BreakerTrip).SelectMany(f => f.Materials).Where(x => w.Ship.CountStored(x.kind) < x.count).ToList();
        if (miss.Count > 0 && !m.Faults.Any(f => f.Stageable)) return $"{Ko.IGa(ItemKinds.Name(miss[0].kind))} 없다 — 오기 전엔 사람을 세우지 않는다";
        return null;
    }
    public override void Begin(World w, FixPlan p, FixStep s)
    {
        var m = M(w);
        if (m == null || !FixSteps.Broken(m)) return;
        var pref = _prefer >= 0 ? w.Crew.FirstOrDefault(c => c.Id == _prefer && c.CanAct) : null;
        // 전문가에게 몰렸다: 그 사람이 다른 계획의 수리를 하고 있으면 이 일은 비어 있는 사람에게 나눈다
        if (pref != null && FixSteps.OnOtherPlan(w, pref.Id, p) && FixSteps.Hand(w, m, new HashSet<int> { pref.Id }, simple: true) is CrewMember other)
        {
            w.Automation.Recovery.Shared++;
            w.Automation.Recovery.Revise(p, $"{Ko.EulReul(pref.Name)} 다른 수리에 붙어 있다 — 일이 한 사람에게 몰리지 않게 {other.Name}에게 나눈다");
            pref = other;
        }
        Ask(w, p, s, m, pref);
    }
    private void Ask(World w, FixPlan p, FixStep s, Machine m, CrewMember? who)
    {
        var a = w.Automation;
        who ??= FixSteps.Hand(w, m, _late);
        var o = Order(w, m);
        if (who == null || o == null) { s.Note = o == null ? "수리 일감이 아직 안 올라왔다" : "맡을 사람이 없다"; return; }
        s.Crew = who.Id;
        s.Order = o.Id;
        s.Mark = w.Tick;
        a.CrewModel.Ask(who, o, a.Manner.Brief(who, m, p, s), 3f, crisis: true);
        a.Command.Line(CmdTarget.Crew, who.Id, m.Body.Room, $"{who.Name}: {m.Name} 수리", p.Goal, 0.85f, s.Max, workOrder: o.Id, state: "하는 중");
        s.Note = $"{who.Name}에게 부탁";
    }
    public override FixState Tick(World w, FixPlan p, FixStep s)
    {
        var m = M(w);
        if (m == null) { s.Note = "설비가 없어졌다"; return FixState.Failed; }
        if (!FixSteps.Broken(m))
        {
            var who = w.Crew.FirstOrDefault(c => c.Id == s.Crew);
            if (who != null) w.Automation.Review.Work(who, m, s.Took(w.Tick), s.Min, s.Max);
            s.Note = $"고쳤다 ({s.Took(w.Tick):0}분)" + (m.Faults.Any(f => f.Stage > 0) ? " — 임시 운전" : "");
            return FixState.Done;
        }
        var o = Order(w, m);
        if (s.Crew < 0 && o != null && s.Took(w.Tick) >= 1f) Ask(w, p, s, m, null);
        // 부탁한 사람이 안 온다: 잠들었거나 · 쓰러졌거나 · 20분이 넘도록 손대지 않았다 → 다른 사람에게 넘기고 다시 짠다
        var asked = w.Crew.FirstOrDefault(c => c.Id == s.Crew);
        bool working = asked != null && asked.Job?.Order == o;
        if (s.Crew >= 0 && o != null && !working && (o.Assignee == null || o.Assignee == asked) && s.Late < 3
            && (asked == null || !asked.CanAct || asked.Pose == Pose.Sleeping || w.Tick - s.Mark > SimTime.Minutes(20)) && Late(w, p, s) is string why)
        {
            s.Late++;
            w.Automation.Recovery.Late++;
            w.Automation.Recovery.Revise(p, why);
        }
        if (o?.Assignee is CrewMember on && on.Id != s.Crew)
        {
            var prev = w.Crew.FirstOrDefault(c => c.Id == s.Crew);
            s.Crew = on.Id;
            s.Note = $"{Ko.IGa(on.Name)} 맡았다";
            if (prev != null && s.Took(w.Tick) > 2f)
            {
                // 하던 사람이 손을 놓았다 → 이어받은 사람의 속도로 남은 시간을 다시 잰다
                var f0 = m.Faults.Where(f => f.Kind != FaultKind.BreakerTrip).OrderBy(f => f.OutputFactor).FirstOrDefault();
                float left = (f0?.Spec.RepairHours ?? 1f) * 60f * (1.15f - 0.4f * on.SkillLevel(m.Spec.Skill)) * w.Automation.Review.Values.PaceOf(on.Id) * (1f - (o.Progress));
                s.Min = s.Took(w.Tick) + left * 0.8f;
                s.Max = s.Took(w.Tick) + left * 1.3f + 10f;
                s.Late++;
                w.Automation.Recovery.Late++;
                w.Automation.Recovery.Revise(p, $"{Ko.IGa(prev.Name)} 손을 놓아 늦어진다 — {Ko.IGa(on.Name)} 이어받았다 · 남은 시간을 {left * 0.8f:0}~{left * 1.3f + 10f:0}분으로 다시 잰다");
            }
        }
        if (o?.Assignee != null) s.Waiting = "";
        else if (s.Crew >= 0) s.Waiting = $"{Ko.IGa(w.Crew.FirstOrDefault(c => c.Id == s.Crew)?.Name ?? "?")} 오기를 기다림";
        // 현장에서 "안 된다" — 부품 · 길 · 위험
        if (o?.BlockedReason is string br && o.BlockedUntil > w.Tick && s.Took(w.Tick) > 3f)
        {
            if (br.Contains("없음") && m.Faults.All(f => !f.Stageable)) { s.Note = $"현장: {br}"; return FixState.Failed; }
            s.Waiting = $"현장: {br}";
        }
        return FixState.Run;
    }
    public override string? Late(World w, FixPlan p, FixStep s)
    {
        var m = M(w);
        if (m == null) return null;
        var o = Order(w, m);
        if (o?.Assignee != null && o.Assignee.Id == s.Crew && o.Assignee.Job?.Order == o)
            return $"{Ko.IGa(o.Assignee.Name)} 고치는 중인데 예상보다 길다 — 감출력을 유지하며 기다린다";
        var late = w.Crew.FirstOrDefault(c => c.Id == s.Crew);
        if (late != null) { _late.Add(late.Id); if (o != null) w.Board.Release(o, late); }
        var next = FixSteps.Hand(w, m, _late);
        if (next == null) return late != null ? $"{Ko.IGa(late.Name)} 늦는데 맡길 다른 사람이 없다 — 기다린다" : null;
        Ask(w, p, s, m, next);
        // 늦어진 만큼 시간 벌기 걸음은 그대로 (감출력 · 예비 펌프)
        return late != null ? $"{Ko.IGa(late.Name)} 늦다 — {next.Name}에게 넘기고, 감출력을 그만큼 더 유지한다" : $"아무도 안 왔다 — {next.Name}에게 부탁했다";
    }
}

/// <summary>시험 운전: 고친 설비를 짧게 돌려 기능을 본다 (사람이 붙어 있으면 재기동 잠금).</summary>
internal sealed class TestRunStep : FixAction
{
    private readonly int _furn;
    public TestRunStep(int furn) => _furn = furn;
    public override int Target => _furn;
    public override string Name => "시험 운전";
    public override FixKind Kind => FixKind.Test;
    public override float Valid => 10f;
    public override (float min, float max) Span(World w, FixCase c) => (3f, 5f);
    private Furniture? F(World w) => w.Ship.Furniture.FirstOrDefault(f => f.Id == _furn);
    public override string? Ready(World w, FixPlan p, FixStep s)
    {
        var f = F(w);
        if (f?.Machine is not Machine m) return null;
        if (FixSteps.Broken(m)) return "아직 고장 — 수리를 기다림";
        if (FixBook.WorkLock(w, m)) return "사람이 작업 중 — 재기동 잠금";
        if (w.Automation.Core.Reach(f.Room) < 2) return "구역과 끊김 — 명령을 보낼 수 없다";
        return null;
    }
    public override void Begin(World w, FixPlan p, FixStep s)
    {
        var f = F(w);
        if (f == null) return;
        s.IssuedAt = w.Tick;
        if (f.Type == FurnitureType.CoolantPump) w.Automation.Recovery.SetDrive(f, 0.6f);
        w.Automation.Command.Line(CmdTarget.Machine, f.Id, f.Room, $"{f.Name} 시험 운전", "고친 것이 실제로 도는지 본다", 0.6f, 5f, state: "하는 중");
    }
    public override FixState Tick(World w, FixPlan p, FixStep s)
    {
        var f = F(w);
        if (f?.Machine is not Machine m) { s.Note = "설비가 없어졌다"; return FixState.Failed; }
        if (s.Took(w.Tick) < 3f) return FixState.Run;
        var a = w.Automation;
        if (f.Type == FurnitureType.CoolantPump)
        {
            a.Recovery.SetDrive(f, 1f);
            float flow = a.Probe.Reading(f);
            float want = 0.75f * a.Probe.Expected(f);
            if (flow < want) { s.Note = $"시험 운전에서 유량 {flow * 100:0}% — 아직이다"; a.Probe.Suspect(f, "시험 운전 유량 낮음"); return FixState.Failed; }
            s.Note = $"유량 {flow * 100:0}%";
            return FixState.Done;
        }
        if (m.Efficiency < 0.45f || FixSteps.Broken(m)) { s.Note = $"돌려 보니 {m.Efficiency * 100:0}%밖에 안 낸다"; return FixState.Failed; }
        s.Note = $"{m.Efficiency * 100:0}% 낸다";
        return FixState.Done;
    }
    public override void Cancel(World w, FixPlan p, FixStep s) { if (F(w) is Furniture f) w.Automation.Recovery.SetDrive(f, 1f); }
}

/// <summary>차례로 정상화: 노심 온도를 보며 출력을 한 단씩 올리고, 마지막에 예비 펌프 세기를 되돌린다.</summary>
internal sealed class RampStep : FixAction
{
    public override string Name => "차례로 정상화";
    public override float CapSet => 1f;
    public override float Valid => 15f;
    public override (float min, float max) Span(World w, FixCase c) => (9f, 15f);
    public override FixState Tick(World w, FixPlan p, FixStep s)
    {
        var a = w.Automation;
        var book = a.Recovery;
        float T = w.Power.ReactorTemperature;
        if (!w.Power.ReactorOnline) { book.ReactorCap = 1f; s.Note = "원자로가 꺼져 있다 — 올릴 것이 없다"; return FixState.Done; }
        if (T > PowerGrid.OverheatWarnC - 5f) { s.Note = $"올리다 다시 달아오른다 (노심 {T:0}℃)"; return FixState.Failed; }
        if (T > PowerGrid.OverheatWarnC - 25f) { s.Waiting = $"노심 {T:0}℃ — 올리기를 멈추고 지켜본다"; return FixState.Run; }
        s.Waiting = "";
        if (w.Tick - s.Mark >= SimTime.Minutes(3) || s.Mark == 0)
        {
            s.Mark = w.Tick;
            book.ReactorCap = MathF.Min(1f, book.ReactorCap + 0.2f);
            a.Command.Line(CmdTarget.Reactor, w.Power.Reactor?.Body.Id ?? -1, w.Power.Reactor?.Body.Room, $"원자로 출력 {book.ReactorCap * 100:0}%로", "냉각이 돌아왔다 — 한 단씩 올린다", 0.5f, 5f);
        }
        if (book.ReactorCap >= 0.999f && s.Took(w.Tick) >= 3f)
        {
            foreach (var f in w.Ship.FurnitureOf(FurnitureType.CoolantPump)) book.SetDrive(f, 1f);
            s.Note = $"출력 100% · 노심 {T:0}℃";
            return FixState.Done;
        }
        return FixState.Run;
    }
}

/// <summary>재고장 감시: 고친 뒤 버티는지 본다 (임시 부품 수명을 잰다).</summary>
internal sealed class WatchStep : FixAction
{
    private readonly int _furn;
    public WatchStep(int furn) => _furn = furn;
    public override string Name => "재고장 감시";
    public override FixKind Kind => FixKind.Watch;
    public override (float min, float max) Span(World w, FixCase c) => (20f, 20f);
    public override FixState Tick(World w, FixPlan p, FixStep s)
    {
        var m = w.Ship.Furniture.FirstOrDefault(f => f.Id == _furn)?.Machine;
        if (m == null) return FixState.Done;
        if (FixSteps.Broken(m)) { s.Note = $"다시 멎었다 — 고친 지 {s.Took(w.Tick):0}분"; w.Automation.Review.Refail(m, s.Took(w.Tick)); return FixState.Failed; }
        if (s.Took(w.Tick) >= 20f) { s.Note = "20분 버틴다"; return FixState.Done; }
        return FixState.Run;
    }
}

/// <summary>한 겹 뒤 문 닫기: 저쪽 방이 비었을 때만 (사람을 가두지 않는다).</summary>
internal sealed class DoorBackStep : FixAction
{
    private readonly int _door, _low;
    private readonly List<int> _locked = new();
    public DoorBackStep(int door, int low) { _door = door; _low = low; }
    public override string Name => "한 겹 뒤 문 닫기";
    public override (float min, float max) Span(World w, FixCase c) => (1f, 3f);
    private Room? High(World w)
    {
        var d = w.Ship.Doors.FirstOrDefault(x => x.Id == _door);
        return d == null ? null : d.RoomA?.Id == _low ? d.RoomB : d.RoomA;
    }
    public override string? Ready(World w, FixPlan p, FixStep s)
    {
        var high = High(w);
        if (high == null) return null;
        var inside = w.Crew.Where(c => !c.Dead && c.Room == high).ToList();
        if (inside.Count == 0) return null;
        if (w.Tick - s.Mark > SimTime.Minutes(2))
        {
            s.Mark = w.Tick;
            foreach (var c in inside) w.Automation.Command.Wake(c, $"{high.Name} 문을 닫아야 한다 — 나와 주십시오", high);
        }
        return $"{high.Name}에 {inside.Count}명 — 먼저 내보낸다";
    }
    public override void Begin(World w, FixPlan p, FixStep s)
    {
        var high = High(w);
        if (high == null || s.Issued) return;
        s.Issued = true;
        s.IssuedAt = w.Tick;
        foreach (var d in high.Doors)
        {
            if (d.Id == _door || d.IsExternal || d.Removed || d.Locked) continue;
            d.Locked = true;
            _locked.Add(d.Id);
        }
        if (high.VentOpen && !high.DamperJammed && !high.DamperStuck && w.Automation.DampersIn(high)) high.VentOpen = false;
        w.Automation.Command.Line(CmdTarget.Door, _door, high, $"{high.Name} 문 {_locked.Count}개 닫기", "새는 쪽 문이 안 닫힌다 — 한 겹 뒤에서 막는다", 0.8f, 20f);
    }
    public override FixState Tick(World w, FixPlan p, FixStep s)
    {
        if (s.Took(w.Tick) < 1f) return FixState.Run;
        int open = w.Ship.Doors.Count(d => _locked.Contains(d.Id) && d.Openness > 0.1f);
        s.Note = open == 0 ? $"뒤 문 {_locked.Count}개 닫힘" : $"뒤 문 {open}개가 아직 열림";
        return open == 0 || s.Took(w.Tick) >= 3f ? FixState.Done : FixState.Run;
    }
    internal void Release(World w)
    {
        foreach (var d in w.Ship.Doors)
            if (_locked.Contains(d.Id) && !(d.RoomA?.Lockdown ?? false) && !(d.RoomB?.Lockdown ?? false)) d.Locked = false;
        _locked.Clear();
    }
}

/// <summary>손으로 닫기: 지렛대 든 사람에게 (휜 문 · 끼인 잔해).</summary>
internal sealed class CrankStep : FixAction
{
    private readonly int _door;
    private readonly HashSet<int> _late = new();
    public CrankStep(int door) => _door = door;
    public override string Name => "손으로 닫기";
    public override FixKind Kind => FixKind.Hands;
    public override (float min, float max) Span(World w, FixCase c) => (6f, 25f);
    private Door? D(World w) => w.Ship.Doors.FirstOrDefault(x => x.Id == _door);
    private WorkOrder? O(World w) => w.Board.All.FirstOrDefault(o => !o.Closed && o.Kind is WorkKind.CrankDoor or WorkKind.RepairDoor && o.Target.Door?.Id == _door);
    private void Ask(World w, FixPlan p, FixStep s)
    {
        var o = O(w);
        var d = D(w);
        if (o == null || d == null) return;
        CrewMember? best = null;
        float bd = float.MaxValue;
        foreach (var c in w.Crew)
        {
            if (!c.CanAct || c.IsChild || c.Outside || _late.Contains(c.Id) || c.Needs.Rest < 0.15f) continue;
            float dist = (c.Position - d.Cell.Center).Length() - 4f * c.SkillLevel(Skill.Mechanics);
            if (dist < bd || dist == bd && best != null && c.Id < best.Id) { bd = dist; best = c; }
        }
        if (best == null) return;
        s.Crew = best.Id;
        s.Order = o.Id;
        w.Automation.CrewModel.Ask(best, o, w.Automation.Manner.Brief(best, null, p, s), 1f, crisis: true);
        w.Automation.Command.Line(CmdTarget.Crew, best.Id, d.RoomA, $"{best.Name}: 문 손으로 닫기", p.Goal, 0.9f, 20f, workOrder: o.Id, state: "하는 중");
    }
    public override void Begin(World w, FixPlan p, FixStep s) => Ask(w, p, s);
    public override FixState Tick(World w, FixPlan p, FixStep s)
    {
        var d = D(w);
        if (d == null || d.Removed) { s.Note = "문이 없어졌다"; return FixState.Done; }
        if (d.Openness < 0.08f) { s.Note = $"닫힘 확인 ({s.Took(w.Tick):0}분)"; return FixState.Done; }
        if (!(w.Ship.Rooms.FirstOrDefault(r => r.Id == p.RoomId)?.Lockdown ?? false)) { s.Note = "새는 게 멎었다 — 닫을 까닭이 없어졌다"; return FixState.Done; }
        if (s.Crew < 0 && s.Took(w.Tick) >= 1f) Ask(w, p, s);
        s.Waiting = s.Crew >= 0 ? $"{Ko.IGa(w.Crew.FirstOrDefault(c => c.Id == s.Crew)?.Name ?? "?")} 지렛대로 닫기를 기다림" : "손 닫기 일감을 기다림";
        return FixState.Run;
    }
    public override string? Late(World w, FixPlan p, FixStep s)
    {
        var late = w.Crew.FirstOrDefault(c => c.Id == s.Crew);
        if (late != null) _late.Add(late.Id);
        Ask(w, p, s);
        var now = w.Crew.FirstOrDefault(c => c.Id == s.Crew);
        return late != null && now != null && now != late ? $"{Ko.IGa(late.Name)} 늦다 — {now.Name}에게 넘겼다" : "아직 아무도 못 닫았다 — 계속 부른다";
    }
}

/// <summary>한 겹 뒤 문 다시 열기 (새는 문이 닫혔거나 새는 게 멎으면).</summary>
internal sealed class DoorReleaseStep : FixAction
{
    private readonly int _door, _low;
    public DoorReleaseStep(int door, int low) { _door = door; _low = low; }
    public override string Name => "뒤 문 다시 열기";
    public override (float min, float max) Span(World w, FixCase c) => (1f, 1f);
    public override FixState Tick(World w, FixPlan p, FixStep s)
    {
        FixSteps.Cleanup(w, p);
        s.Note = "길을 다시 열었다";
        return FixState.Done;
    }
}
