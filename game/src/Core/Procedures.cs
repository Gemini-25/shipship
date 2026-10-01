using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

/// <summary>정비 절차의 한 단계.</summary>
public enum ProcStep { Lockout, Isolate, Bleed, Repair, Test, Restart }

/// <summary>한 번의 수리를 어떻게 할지: 할 단계와 건너뛴 단계(와 까닭).</summary>
public sealed class ProcPlan
{
    public List<ProcStep> Steps { get; } = new();
    public List<(ProcStep step, string why)> Skipped { get; } = new();
    public bool Derate { get; set; }        // 원자로 출력을 잠시 낮춘다 (냉각 펌프를 끄려면)
    public bool Hot { get; set; }           // 전원을 산 채로 고친다 (대신할 설비가 없다)
    public bool UsedPart { get; set; }      // 떼어 온 중고 부품을 쓴다

    public static string Name(ProcStep s) => s switch
    {
        ProcStep.Lockout => "전원 차단", ProcStep.Isolate => "밸브 격리", ProcStep.Bleed => "잔압 제거",
        ProcStep.Repair => "수리", ProcStep.Test => "시험 운전", _ => "재가동",
    };

    public string Summary =>
        string.Join("→", Steps.Select(Name)) + (Skipped.Count > 0 ? " · 생략: " + string.Join(", ", Skipped.Select(x => $"{Name(x.step)}({x.why})")) : "")
        + (Derate ? " · 원자로 출력을 낮추고" : "") + (UsedPart ? " · 중고 부품" : "");
}

public sealed class ProcedureStats
{
    public int Full;         // 절차를 다 밟은 수리
    public int Skipped;      // 한 단계라도 건너뛴 수리
    public int HotWork;      // 전원을 산 채로
    public int Shocks;       // 감전·증기 화상
    public int Defects;      // 재조립 불량 (시험 운전을 건너뛰어 나중에 재발)
    public int CaughtByTest; // 시험 운전에서 잡은 불량
    public int Derates;      // 원자로 출력을 낮춘 수리
    public int Rewired;      // 설비 전선을 다시 걸었다
    public int Spliced;      // 임시로 이어 붙였다
    public int Relined;      // 설비 관을 다시 이었다
    public int WaterHammer;  // 급히 되살리다 관이 터졌다

    public override string ToString() =>
        $"절차 다 밟음 {Full} · 생략 {Skipped}(산 채로 {HotWork} · 감전·화상 {Shocks}) · 재조립 불량 {Defects}(시험 운전에서 잡음 {CaughtByTest}) · " +
        $"원자로 낮춤 {Derates} · 전선 다시 {Rewired}(임시 {Spliced}) · 관 다시 {Relined} · 워터해머 {WaterHammer}";
}

/// <summary>
/// v12.1 정비 절차 · 설비마다 전선과 관 · 중고 부품 · 미뤄 둔 정비.
/// - 절차: 전원 차단 → 밸브 격리 → 잔압 제거 → 수리 → 시험 운전 → 재가동. 꼼꼼한 사람·평시에는 다 밟고,
///   급하거나 대신할 설비가 없으면 건너뛴다 — 산 채로 고치면 감전(전기 설비)·증기 화상(관이 달린 설비)이 날 수 있고,
///   시험 운전을 건너뛰면 재조립 불량이 몇 시간 뒤 같은 고장으로 돌아온다. 떼어 온 중고 부품은 불량이 더 잦다.
/// - 대신할 것: 산소 발생기를 끄려면 다른 발생기나 공기 탱크가 버텨야 하고, 냉각 펌프를 끄려면 남은 펌프로 원자로를 식힐 만큼 출력을 낮춘다.
/// - 급히 되살리기: 관이 달린 설비를 한꺼번에 돌리면 압력이 튀어 약한 관·땜질이 터진다 (워터해머).
/// - 설비마다 전선(Feed)과 관(Line): 불·폭발·충돌에 설비와 따로 끊긴다. 멀쩡한 설비에 전기·물이 안 들어온다.
///   정전·비상이면 임시로 이어 붙이고(미뤄 둔 정비), 평온해지면 케이블로 정식으로 다시 건다.
/// </summary>
public static class Procedures
{
    /// <summary>관이 달린 설비 (격리·잔압 제거가 필요하다).</summary>
    public static bool Plumbed(FurnitureType t) => t is FurnitureType.CoolantPump or FurnitureType.WaterRecycler or FurnitureType.HeatExchanger or FurnitureType.OxygenGenerator
        or FurnitureType.GrowBed or FurnitureType.EngineCore;

    /// <summary>이 수리를 어떻게 할지 정한다 (사람의 꼼꼼함, 급함, 대신할 설비).</summary>
    public static ProcPlan Plan(World w, CrewMember c, Machine m, bool urgent)
    {
        var p = new ProcPlan();
        float thorough = WatchLog.Thoroughness(c);
        var t = m.Body.Type;
        bool electrical = m.Spec.PowerDraw > 0f || t is FurnitureType.PowerPanel or FurnitureType.Battery;
        // 전원 차단: 대신할 것이 있어야 끈다
        string? noSpare = NoSpare(w, m, out bool derate);
        if (noSpare != null && !derate) { p.Hot = true; p.Skipped.Add((ProcStep.Lockout, noSpare)); }
        else
        {
            if (derate) p.Derate = true;
            if (electrical) p.Steps.Add(ProcStep.Lockout);
        }
        if (Plumbed(t))
        {
            if (urgent && thorough < 0.6f) p.Skipped.Add((ProcStep.Isolate, "급함"));
            else p.Steps.Add(ProcStep.Isolate);
            if (urgent && thorough < 0.75f || p.Hot) p.Skipped.Add((ProcStep.Bleed, p.Hot ? "돌리는 채로" : "급함"));
            else p.Steps.Add(ProcStep.Bleed);
        }
        p.Steps.Add(ProcStep.Repair);
        // 시험 운전: 꼼꼼할수록, 평시일수록 (급해도 성실한 사람은 5분쯤은)
        if (thorough > (urgent ? 0.7f : 0.35f)) p.Steps.Add(ProcStep.Test);
        else p.Skipped.Add((ProcStep.Test, urgent ? "급함" : "괜찮겠지"));
        p.Steps.Add(ProcStep.Restart);
        p.UsedPart = w.UsedParts.Count > 0;
        return p;
    }

    /// <summary>이 설비를 끄면 버틸 수 없는 까닭 (null이면 끌 수 있다). derate: 원자로 출력을 낮추면 끌 수 있다.</summary>
    public static string? NoSpare(World w, Machine m, out bool derate)
    {
        derate = false;
        var t = m.Body.Type;
        int others = w.Ship.FurnitureOf(t).Count(f => f.Machine != m && f.Machine is { Stopped: false, Efficiency: > 0.3f });
        switch (t)
        {
            case FurnitureType.OxygenGenerator:
                return others == 0 && w.Air.Reserve < w.Air.ReserveCapacity * 0.3f ? "대신할 발생기도 탱크 여유도 없다" : null;
            case FurnitureType.CoolantPump:
                if (others > 0 || !w.Power.ReactorOnline) return null;
                derate = true;
                return "마지막 냉각 펌프";
            case FurnitureType.PowerPanel:
                return "배전반을 끄면 온 배가 정전";
            case FurnitureType.MainComputer:
                return others == 0 && !w.Automation.Backup ? "대신할 제어기가 없다" : null;
            default:
                return null;
        }
    }

    /// <summary>수리 전 단계들 (차단·격리·잔압): 시간과 위험.</summary>
    public static void Before(World w, CrewMember c, Machine m, ProcPlan p, List<Toil> toils, WorkOrder o)
    {
        float prep = 0f;
        if (p.Steps.Contains(ProcStep.Lockout)) prep += 0.05f;
        if (p.Steps.Contains(ProcStep.Isolate)) prep += 0.08f;
        if (p.Steps.Contains(ProcStep.Bleed)) prep += 0.08f;
        if (p.Derate) prep += 0.05f;
        toils.Add(new DoToil((cm, world) =>
        {
            if (p.Derate) { world.Power.MaintenanceCap = true; world.Procs.Derates++; }
            if (p.Steps.Contains(ProcStep.Lockout)) { m.Parked = true; m.LockedOut = true; }
            world.Log.Add(world.Tick, LogKind.Work, $"{m.Name} 절차: {p.Summary}", cm.Id);
            return true;
        }));
        if (prep > 0f) toils.Add(new WorkToil(prep, m.Spec.Skill, m.Body.Center) { Resume = null });
    }

    /// <summary>수리를 마친 뒤: 감전·증기(생략했으면), 시험 운전, 재가동(워터해머), 재조립 불량.</summary>
    public static void After(World w, CrewMember c, Machine m, ProcPlan p, FaultKind fixedFault)
    {
        var st = w.Procs;
        if (p.Skipped.Count > 0) st.Skipped++; else st.Full++;
        if (p.Hot) st.HotWork++;
        // 산 채로·잔압 그대로 고쳤다
        bool plumbed = Plumbed(m.Body.Type);
        float risk = (p.Hot ? 0.18f : 0f) + (plumbed && p.Skipped.Any(x => x.step == ProcStep.Bleed) ? 0.12f : 0f);
        if (risk > 0f && w.Rng.Chance(risk * (1.2f - c.SkillLevel(m.Spec.Skill))))
        {
            st.Shocks++;
            string what = p.Hot && m.Spec.PowerDraw > 0f ? "감전" : "증기 화상";
            float dmg = w.Rng.Range(0.08f, 0.2f);
            c.Vitals.Health = MathF.Max(0.05f, c.Vitals.Health - dmg);
            NeedsSystem.AddInjury(c.Vitals, dmg, what);
            MarkLog.Add(c.Memory.Marks, w.Tick, $"{m.Name}을(를) 고치다 {what}");
            w.Log.Add(w.Tick, LogKind.Warning, $"{m.Name}을(를) {(p.Hot ? "산 채로" : "잔압을 빼지 않고")} 고치다 {what}", c.Id);
        }
        // 재조립 불량: 시험 운전을 했으면 대개 그 자리에서 잡는다
        float defect = 0.08f + (p.UsedPart ? 0.2f : 0f) + (c.Needs.Rest < 0.25f ? 0.08f : 0f) + 0.1f * (1f - c.SkillLevel(m.Spec.Skill));
        if (p.UsedPart && w.UsedParts.Count > 0) w.UsedParts.RemoveAt(0);
        if (w.Rng.Chance(defect))
        {
            if (p.Steps.Contains(ProcStep.Test) && w.Rng.Chance(0.9f))
            {
                st.CaughtByTest++;
                w.Log.Add(w.Tick, LogKind.Work, $"{m.Name} 시험 운전 — 소리가 이상해 다시 조였다 (재조립 불량을 잡았다)", c.Id);
                MarkLog.Add(m.Marks, w.Tick, $"{c.Name}: 시험 운전에서 불량을 잡았다");
            }
            else
            {
                m.Defect = fixedFault;
                m.DefectDue = w.Tick + SimTime.Hours(w.Rng.Range(3f, 14f));
                m.DefectBy = c.Name;
            }
        }
        // 재가동: 관이 달린 설비를 급히 돌리면 압력이 튄다 (약한 관·땜질이 터진다)
        if (plumbed && p.Skipped.Count > 0 && w.Rng.Chance(0.12f))
        {
            var seg = w.Piping.Segments.Where(s => s.Patched || s.Integrity < 0.7f).OrderBy(s => s.Integrity).FirstOrDefault();
            if (seg != null && seg.Path.Count > 0)
            {
                st.WaterHammer++;
                w.Piping.Damage(seg, 0.4f, seg.Path[seg.Path.Count / 2], $"{m.Name}을(를) 급히 되살리다 압력이 튀었다 (워터해머)");
            }
        }
        if (m.LockedOut) { m.LockedOut = false; m.Parked = false; }
        if (p.Derate) w.Power.MaintenanceCap = false;
        w.Parts.OnFixed(m, fixedFault, c, p.UsedPart); // v14.6 그 고장을 맡은 부품만 갈고, 일찍 나갔으면 묶음을 · 또 나갔으면 원인을
    }

    /// <summary>시스템 틱: 재조립 불량이 때가 되면 같은 고장으로 돌아온다.</summary>
    public static void Update(World w)
    {
        // 수리가 끊겼으면 잠가 둔 설비·낮춘 원자로를 되돌린다
        bool repairing(Machine mm) => w.Crew.Any(c => c.Job?.Order is WorkOrder o && o.Kind == WorkKind.Repair && o.Target.Furniture == mm.Body);
        foreach (var m in w.Ship.Machines)
            if (m.LockedOut && !repairing(m)) { m.LockedOut = false; m.Parked = false; }
        if (w.Power.MaintenanceCap && !w.Ship.FurnitureOf(FurnitureType.CoolantPump).Any(f => repairing(f.Machine!))) w.Power.MaintenanceCap = false;
        foreach (var m in w.Ship.Machines)
        {
            if (m.Defect is not FaultKind k || w.Tick < m.DefectDue) continue;
            m.Defect = null;
            w.Procs.Defects++;
            if (m.Faults.Count == 0) w.Machines.Break(m, k);
            MarkLog.Add(m.Marks, w.Tick, $"재조립 불량 — {m.DefectBy}이(가) 시험 운전 없이 올린 뒤 다시 {Faults.Spec(k).Name}");
            w.History.Add(w, HistoryKind.Maintenance, $"{m.Name}이(가) 다시 {Faults.Spec(k).Name} — {m.DefectBy}이(가) 시험 운전을 건너뛰고 올렸다", m.Body.Room);
        }
    }

    /// <summary>불·폭발·충돌이 설비의 전선과 관을 상하게 한다.</summary>
    public static void DamageLinks(World w, Machine m, float feed, float line, string cause)
    {
        float f0 = m.Feed, l0 = m.Line;
        m.Feed = MathF.Max(0f, m.Feed - feed);
        if (Plumbed(m.Body.Type)) m.Line = MathF.Max(0f, m.Line - line);
        if (f0 >= 0.3f && m.Feed < 0.3f)
        {
            MarkLog.Add(m.Marks, w.Tick, $"전선이 끊겼다 ({cause})");
            w.Log.Add(w.Tick, LogKind.Warning, $"{m.Name} 전선이 끊겼다 — 설비는 멀쩡한데 전기가 안 들어간다 ({cause})");
            w.Board.RequestScan();
        }
        if (l0 >= 0.3f && m.Line < 0.3f)
        {
            MarkLog.Add(m.Marks, w.Tick, $"관 이음이 빠졌다 ({cause})");
            w.Log.Add(w.Tick, LogKind.Warning, $"{m.Name} 관 이음이 빠졌다 — 물·냉각수가 안 들어간다 ({cause})");
            w.Board.RequestScan();
        }
    }
}

/// <summary>미뤄 둔 정비 한 줄 (사고는 끝났는데 배에 남은 일).</summary>
public sealed record DeferredItem(string What, string Where, string Why, string Risk, WorkTarget? Target);

public static class Deferred
{
    /// <summary>지금 배에 남은 임시 복구·미룬 일 (상태에서 바로 모은다).</summary>
    public static List<DeferredItem> Items(World w)
    {
        var list = new List<DeferredItem>();
        var ship = w.Ship;
        foreach (var m in ship.Machines)
        {
            string where = m.Body.Room.Name;
            foreach (var f in m.Faults.Where(f => f.Stage > 0))
                list.Add(new($"{m.Name} {f.Spec.Name}", where, f.StageName, "임시로 살려 빨리 닳는다", WorkTarget.Of(m.Body)));
            if (m.Grade == MachineGrade.Mk1) list.Add(new($"{m.Name} Mk.1 임시품", where, "정품 부품이 없었다", "출력 65% · 고장 두 배", WorkTarget.Of(m.Body)));
            if (m.Spliced) list.Add(new($"{m.Name} 임시 전선", where, "정전 중 이어 붙였다", "합선·불 위험", WorkTarget.Of(m.Body)));
            if (m.Defect != null) list.Add(new($"{m.Name} 시험 운전 안 함", where, $"{m.DefectBy}이(가) 급히 올렸다", "재조립 불량이 숨어 있을지 모른다", WorkTarget.Of(m.Body)));
            if (m.Fouled > 0.3f) list.Add(new($"{m.Name} 소화 분말", where, "불을 끄고 남은 것", "효율 저하 · 합선", WorkTarget.Of(m.Body)));
            if (m.SensorCal < 0.7f) list.Add(new($"{m.Name} 감지기 교정 {m.SensorCal * 100:0}%", where, "오래 못 맞췄다", "계기 오류 · 전조를 놓친다", WorkTarget.Of(m.Body)));
        }
        foreach (var j in w.Power.Jumpers.Where(j => j.Active && !j.Permanent))
            list.Add(new($"임시 배선 {PowerGrid.CircuitName(j.From)}→{PowerGrid.CircuitName(j.To)}", "배전반", "회로를 살리려 끌어왔다", "정격을 넘기면 달아오른다", null));
        foreach (var s in w.Piping.Segments)
        {
            if (s.Patched) list.Add(new($"{s.Name} 클램프 땜질", "배관", "급히 막았다", "압력이 튀면 다시 샌다", null));
            if (s.Bypass > 0f) list.Add(new($"{s.Name} 우회 배관", "배관", "끊긴 관을 돌렸다", $"흐름 {s.Bypass * 100:0}%", null));
        }
        int patched = ship.Walls.Count(kv => kv.Value.IsHull && kv.Value.Patched);
        if (patched > 0) list.Add(new($"외벽 실링폼 봉합 {patched}곳", "선체", "급히 막았다", "시간이 지나면 떨어진다 — 용접해야 한다", null));
        if (ship.Rubble.Count > 0) list.Add(new($"잔해 {ship.Rubble.Count}칸", "선내", "폭발", "길·문을 막는다", null));
        foreach (var r in ship.Rooms.Where(r => r.Abandoned && !r.Detached)) list.Add(new($"{r.Name} 봉쇄", r.Name, r.AbandonReason ?? "", "되찾아야 한다", WorkTarget.OfRoom(r)));
        return list;
    }
}

public sealed partial class WorkBoard
{
    /// <summary>v12.1 설비 전선·관 다시 걸기.</summary>
    private void ScanLinks(Poster post)
    {
        var w = _world;
        bool calm = Crisis.Level(w) < CrisisLevel.Emergency;
        foreach (var m in w.Ship.Machines)
        {
            if (m.Body.Room.Abandoned || m.Body.Room.OffLimits || m.Body.Room.Leaking) continue;
            float u = m.Spec.Critical ? 0.9f : 0.5f;
            if (m.Feed < 0.5f)
                post(WorkKind.Rewire, WorkTarget.Of(m.Body), u, Skill.Electrical, m.Feed < 0.3f ? $"전선 끊김 ({m.Feed * 100:0}%) — 전기가 안 들어간다" : $"전선 상함 ({m.Feed * 100:0}%)");
            else if (m.Spliced && calm)
                post(WorkKind.Rewire, WorkTarget.Of(m.Body), 0.3f, Skill.Electrical, "임시로 이어 붙인 전선 → 케이블로 정식 배선");
            if (m.Line < 0.5f && Procedures.Plumbed(m.Body.Type))
                post(WorkKind.Reline, WorkTarget.Of(m.Body), u - 0.05f, Skill.Mechanics, m.Line < 0.3f ? $"관 이음 빠짐 ({m.Line * 100:0}%) — 물·냉각수가 안 들어간다" : $"관 이음 샘 ({m.Line * 100:0}%)");
        }
    }
}

public static partial class WorkPlanners
{
    /// <summary>설비 전선을 다시 건다: 케이블이 있고 평온하면 정식으로, 비상·케이블 없음이면 임시로 이어 붙인다.</summary>
    private static Job? Rewire(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var m = o.Target.Furniture!.Machine!;
        bool proper = w.Ship.CountStored(ItemKind.Cable) > 0 && (Crisis.Level(w) < CrisisLevel.Emergency || m.Feed >= 0.3f);
        var cost = proper ? new[] { (ItemKind.Cable, 1) } : Array.Empty<(ItemKind, int)>();
        var toils = cost.Length > 0 ? FetchAll(c, w, dist, cost) : Plans.DropOff(c, w, dist);
        if (toils == null) { blocked = "케이블 없음"; return null; }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(proper ? 0.5f : 0.2f, Skill.Electrical, m.Body.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) =>
        {
            if (cost.Length > 0 && !UseAll(cm, cost)) return false;
            world.Board.Close(o);
            if (proper) { m.Feed = 1f; m.Spliced = false; world.Procs.Rewired++; }
            else { m.Feed = 0.65f; m.Spliced = true; world.Procs.Spliced++; }
            MarkLog.Add(m.Marks, world.Tick, $"{cm.Name}: {(proper ? "전선을 새로 걸었다" : "전선을 임시로 이어 붙였다")}");
            world.Log.Add(world.Tick, LogKind.Work, proper ? $"{m.Name} 전선을 새로 걸었다" : $"{m.Name} 전선을 임시로 이어 붙였다 — 평온해지면 케이블로 다시", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, proper ? "전선 다시 걸기" : "임시 배선", toils, $"{m.Name} 전선 {(proper ? "다시 걸기" : "이어 붙이기")}");
    }

    /// <summary>설비 관 이음을 다시 잇는다 (실링폼이나 금속판).</summary>
    private static Job? Reline(Activity a, WorkOrder o, CrewMember c, World w, DistanceField dist, Cell at, out string? blocked)
    {
        blocked = null;
        var m = o.Target.Furniture!.Machine!;
        var cost = w.Ship.CountStored(ItemKind.Sealant) > 0 ? new[] { (ItemKind.Sealant, 1) } : new[] { (ItemKind.Plate, 1) };
        var toils = FetchAll(c, w, dist, cost);
        if (toils == null) { blocked = "실링폼·금속판 없음"; return null; }
        toils.Add(new GotoToil(at));
        toils.Add(new WorkToil(0.4f, Skill.Mechanics, m.Body.Center) { Resume = o });
        toils.Add(new DoToil((cm, world) =>
        {
            if (!UseAll(cm, cost)) return false;
            world.Board.Close(o);
            m.Line = 1f;
            world.Procs.Relined++;
            MarkLog.Add(m.Marks, world.Tick, $"{cm.Name}: 관 이음을 다시 이었다");
            world.Log.Add(world.Tick, LogKind.Work, $"{m.Name} 관 이음을 다시 이었다", cm.Id);
            return true;
        }));
        return Wrap(a, o, c, w, "관 다시 잇기", toils, $"{m.Name} 관 이음 다시 잇기");
    }
}
