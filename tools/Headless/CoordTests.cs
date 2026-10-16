using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

/// <summary>
/// v19 계획 조율 점검 (--coordtest): 여러 계획이 같은 사람 · 부품 · 통로 · 전력 · 배터리를 바랄 때.
/// 감출력은 수술 · 생명유지를 버티는지 본 뒤에 (모자라면 배터리 · 그것도 없으면 수술이 끝날 때까지) · 원자로가 멎은 동안 배터리는 생명유지 → 재기동 → 드론 ·
/// 모자란 부품은 급한 수리부터 (접히면 돌려준다) · 맞는 손이 없으면 덜 급한 일에서 불러오고 인계 메모 · 부상자 이송 길은 지나간 뒤에 닫는다.
/// </summary>
public static partial class Program
{
    private static int RunCoordTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"계획 조율 점검 (v19) · 시드 {seed}\n");
        static List<CrewMember> Able(World w) => w.Crew.Where(c => !c.Dead && !c.Down && !c.IsChild && !c.Away && !c.Outside).OrderBy(c => c.Id).ToList();
        static void Hurt(World w, CrewMember c, float amt, string cause)
        {
            NeedsSystem.AddInjury(c.Vitals, amt, cause);
            Run(w, SimTime.Minutes(1));
            c.Vitals.TreatedTick = w.Tick;
            Run(w, SimTime.Minutes(2));
        }
        static void Until(World w, Func<bool> ok, float hours) { long end = w.Tick + SimTime.Hours(hours); while (w.Tick < end && !ok()) Run(w, SimTime.Minutes(1)); }
        static void SetBattery(World w, float kwh) => w.Power.GetType().GetProperty("BatteryCharge")!.GetSetMethod(true)!.Invoke(w.Power, new object[] { kwh });

        // 1) 수술 중 감출력: 버티면 그대로 · 모자라면 배터리로 메우고 · 배터리도 모자라면 수술이 끝날 때까지 미룬다
        {
            var w = DayOne(seed, "Hanbit");
            var co = w.Automation.Coord;
            var pat = Able(w).Where(c => c.Role != CrewRole.Medic && c.SkillLevel(Skill.Medicine) < 0.3f).First();
            Hurt(w, pat, 0.34f, "작업 중 넘어짐");
            Until(w, () => w.Surgery.Cases.Any(x => x.Patient == pat.Id && x.State == CaseState.Operating), 12f);
            bool operating = w.Surgery.Cases.Any(x => x.Patient == pat.Id && x.State == CaseState.Operating);
            string? half = co.DerateWait(0.8f);
            string? deep = co.DerateWait(0.02f);
            float had = w.Power.BatteryCharge;
            SetBattery(w, 1f);
            string? wait = co.DerateWait(0.02f);
            SetBattery(w, had);
            var lamp = w.Ship.FurnitureOf(FurnitureType.SurgicalLamp).FirstOrDefault()?.Machine;
            Check("감출력 × 수술 — 조금 낮춰 버티면 그대로, 크게 낮추면 모자란 만큼 배터리로, 배터리도 없으면 수술이 끝날 때까지 미룬다",
                operating && half == null && deep == null && wait != null && co.DerateOk >= 1 && co.DerateCovered >= 1 && co.DerateWaits >= 1 && lamp != null && w.Surgery.Holds(lamp),
                $"수술 중 {operating} · 80% {(half ?? "된다")} · 2% {(deep ?? "된다(배터리)")} · 배터리 1kWh면 → {wait} · " + string.Join(" / ", co.Decisions.Select(d => d.text).Where(t => t.Contains("출력")).Take(3)));
            Until(w, () => !w.Surgery.Cases.Any(x => x.State is CaseState.Prep or CaseState.Operating) && !w.Automation.Reserve.MedHold, 8f);
            SetBattery(w, 1f);
            string? after = co.DerateWait(0.02f);
            SetBattery(w, had);
            Check("선행 조건 풀림 — 수술이 끝나면 같은 감출력을 기다리지 않는다", after == null, $"수술 뒤 2% → {(after ?? "된다")}");
        }

        // 2) 원자로가 멎은 동안의 배터리: 생명유지 → 재기동 몫 → 드론 — 남지 않으면 드론 충전을 미루고 덜 급한 드론은 불러들인다
        {
            var w = DayOne(seed, "Hanbit");
            var co = w.Automation.Coord;
            Scenarios.Apply(w, "blackout", out _);
            foreach (var d in w.Drones.Drones) if (d.Operational) d.Battery = 0.3f;
            Until(w, () => !w.Power.ReactorOnline, 1f);
            Run(w, SimTime.Minutes(20));
            var docked = w.Drones.Drones.Where(d => d.Operational && d.State == DroneState.Docked).ToList();
            float b0 = docked.Count > 0 ? docked.Average(d => d.Battery) : 0f;
            Run(w, SimTime.Minutes(30));
            float b1 = docked.Count > 0 ? docked.Average(d => d.Battery) : 0f;
            bool held = docked.Count > 0 && docked.All(d => co.HoldCharge(d)) && b1 <= b0 + 0.01f;
            var dec = co.Decisions.LastOrDefault(d => d.text.StartsWith("배터리")).text ?? "";
            Check("배터리 나눔 — 원자로가 멎고 배터리가 바닥이면 생명유지 · 재기동 몫을 먼저 남기고, 드론 충전은 미룬다 (드론 배터리가 그대로)",
                !w.Power.ReactorOnline && held && dec.Contains("재기동") && co.ChargeHolds >= 1 && w.Automation.Reserve.KeepKwh > 0f,
                $"원자로 {(w.Power.ReactorOnline ? "돎" : "멎음")} · 배터리 {w.Power.BatteryCharge:0.0}/{w.Power.BatteryCapacity:0}kWh · 재기동 몫 {w.Automation.Reserve.KeepKwh:0.0} · 드론 {docked.Count}대 {b0:0.00}→{b1:0.00} · {dec}");
        }

        // 3) 모자란 부품: 같은 부품을 바라는 수리 둘 — 급한 쪽 먼저 · 급한 수리가 접히면 부품이 돌아와 미뤘던 수리가 다시 열린다
        {
            var w = DayOne(seed, "Hanbit");
            var co = w.Automation.Coord;
            (Machine a, Machine b, FaultKind fk, ItemKind part)? pick = null;
            foreach (var m1 in w.Ship.Machines.Where(m => m.Spec.Critical && m.Faults.Count == 0).OrderBy(m => m.Body.Id))
            {
                foreach (var fk in m1.Spec.FaultKinds)
                {
                    if (Faults.Spec(fk).Part is not ItemKind part || part == ItemKind.Plate) continue;
                    var m2 = w.Ship.Machines.Where(m => !m.Spec.Critical && m.Faults.Count == 0 && m.Spec.FaultKinds.Contains(fk)).OrderBy(m => m.Body.Id).FirstOrDefault();
                    if (m2 != null) { pick = (m1, m2, fk, part); break; }
                }
                if (pick != null) break;
            }
            if (pick is not var (ma, mb, fkind, kind)) Check("부품 — 같은 부품을 바라는 핵심 · 보통 설비를 찾지 못했다", false);
            else
            {
                while (w.Ship.CountStored(kind) > 1 && ItemsV15.Use(w, kind)) { }
                w.Machines.Break(mb, fkind); // 같은 때 둘이 고장 — 누가 먼저 계획을 세우든 남은 하나는 급한 쪽 몫
                w.Machines.Break(ma, fkind);
                WorkOrder? oa = null, ob = null;
                for (int i = 0; i < 180 && (oa == null || ob == null); i++)
                {
                    Run(w, 1);
                    oa = w.Board.Open.FirstOrDefault(o => o.Kind == WorkKind.Repair && o.Furniture == ma.Body && !o.Closed);
                    ob = w.Board.Open.FirstOrDefault(o => o.Kind == WorkKind.Repair && o.Furniture == mb.Body && !o.Closed);
                }
                int stock = w.Ship.CountStored(kind);
                // 두 수리 계획이 같은 마지막 부품을 꺼내려 할 때 조율판의 답 (누가 먼저 계획을 세우든 같은 답)
                string? heldB = ob != null && oa != null && stock > 0 ? co.PartHeldFor(ob, kind) : null;
                string? heldA = oa != null && stock > 0 ? co.PartHeldFor(oa, kind) : "부품 없음";
                bool urgentFirst = heldB != null && heldA == null && co.PartWaits >= 1 && co.Decisions.Any(d => d.text.Contains(ItemKinds.Name(kind)) && d.text.Contains("먼저"));
                Check("부품 — 하나 남은 부품은 급한 수리 몫으로 남기고, 덜 급한 수리는 꺼내지 않는다 (부분 수리로 버티거나 까닭을 남기고 기다린다)", urgentFirst,
                    $"{ItemKinds.Name(kind)} {stock}개 · {ma.Name}(급함 {oa?.Urgency:0.##}) → {heldA ?? "꺼낸다"} · {mb.Name}(급함 {ob?.Urgency:0.##}) → {heldB ?? "꺼낸다"} · " + string.Join(" / ", co.Decisions.Select(d => d.text).Where(t => t.Contains(ItemKinds.Name(kind))).Take(2)));
                // 급한 수리가 접힌다 (다른 길로 살렸다) → 부품이 돌아온다
                foreach (var o in w.Board.Open.Where(o => o.Furniture == ma.Body).ToList()) w.Board.Close(o);
                ma.Faults.Clear();
                foreach (var c in w.Crew) if (c.Job?.Order is WorkOrder jo && jo.Furniture == ma.Body) c.Interrupt(w);
                int ret0 = co.PartReturns;
                Until(w, () => co.PartReturns > ret0 || ob != null && (ob.Closed || ob.Assignee != null), 1f);
                Check("반환 — 급한 수리가 부품 없이 끝나면 부품이 돌아오고, 미뤘던 수리가 다시 열린다", co.PartReturns > ret0 || ob != null && ob.Assignee != null,
                    $"돌려줌 {co.PartReturns} · {mb.Name} 수리 {(ob?.Assignee != null ? $"{ob.Assignee.Name}이 맡음" : ob?.BlockedReason ?? "열림")}");
            }
        }

        // 3b) 핵심 설비끼리는 부품 몫으로 서로 막지 않는다 (냉각 펌프 여럿이 한꺼번에 서자 계획의 펌프 수리까지 서로 미루다 원자로가 멎었다)
        {
            var w = DayOne(seed, "Hanbit");
            var co = w.Automation.Coord;
            (Machine a, Machine b, FaultKind fk, ItemKind part)? pick = null;
            foreach (var m1 in w.Ship.Machines.Where(m => m.Spec.Critical && m.Faults.Count == 0).OrderBy(m => m.Body.Id))
            {
                foreach (var fk in m1.Spec.FaultKinds)
                {
                    if (Faults.Spec(fk).Part is not ItemKind part || part == ItemKind.Plate) continue;
                    var m2 = w.Ship.Machines.Where(m => m != m1 && m.Spec.Critical && m.Faults.Count == 0 && m.Spec.FaultKinds.Contains(fk)).OrderBy(m => m.Body.Id).FirstOrDefault();
                    if (m2 != null) { pick = (m1, m2, fk, part); break; }
                }
                if (pick != null) break;
            }
            if (pick is not var (ca, cb, cfk, ckind)) Check("부품(핵심끼리) — 같은 부품을 바라는 핵심 설비 둘을 찾지 못했다", false);
            else
            {
                while (w.Ship.CountStored(ckind) > 1 && ItemsV15.Use(w, ckind)) { }
                w.Machines.Break(ca, cfk);
                w.Machines.Break(cb, cfk);
                WorkOrder? oa = null, ob = null;
                for (int i = 0; i < 180 && (oa == null || ob == null); i++)
                {
                    Run(w, 1);
                    oa = w.Board.Open.FirstOrDefault(o => o.Kind == WorkKind.Repair && o.Furniture == ca.Body && !o.Closed);
                    ob = w.Board.Open.FirstOrDefault(o => o.Kind == WorkKind.Repair && o.Furniture == cb.Body && !o.Closed);
                }
                if (oa != null && ob != null) { oa.Urgency = MathF.Max(oa.Urgency, ob.Urgency + 0.3f); }
                string? held = ob != null && w.Ship.CountStored(ckind) > 0 ? co.PartHeldFor(ob, ckind) : "주문 없음";
                Check("부품(핵심끼리) — 핵심 설비 수리끼리는 남은 부품을 서로의 몫으로 잡아 두지 않는다 (먼저 손댄 쪽이 꺼낸다 — 서로 미루다 둘 다 멈추지 않게)",
                    oa != null && ob != null && held == null,
                    $"{ItemKinds.Name(ckind)} {w.Ship.CountStored(ckind)}개 · {ca.Name}(급함 {oa?.Urgency:0.##}) · {cb.Name}(급함 {ob?.Urgency:0.##}) → {held ?? "꺼낸다"}");
            }
        }

        // 4) 더 급한 일에 맞는 손이 없다 → 덜 급한 일 하던 사람을 부르고 인계 메모를 남긴다
        {
            var w = DayOne(seed, "Hanbit");
            var co = w.Automation.Coord;
            foreach (var rb in w.Robots.Robots) rb.Disabled = true; // 사람 손만 (로봇이 맡아 버리지 않게)
            CrewMember? m = null; WorkOrder? low = null;
            for (int i = 0; i < 6 * 48 && m == null; i++) // 덜 급한 일을 하는 사람 아무나 — 그 사람을 배에서 가장 솜씨 좋은 정비사로 만든다 (그만 핵심 수리에 맞는 손)
            {
                Run(w, SimTime.Minutes(10));
                foreach (var c in Able(w).OrderBy(c => c.Id))
                    if (c.IsAwake && c.CanAct && !c.Outside && c.Suit == null && c.Job?.Order is WorkOrder o && o.Urgency < 0.6f && o.Progress > 0.05f && o.Progress < 0.5f && o.Kind is not (WorkKind.Maintain or WorkKind.PreventiveCheck) && c.Job.Urgent != true) { m = c; low = o; break; } // 절반 넘게 남은 긴 일 (몇 분짜리 점검은 급한 고장이 뜨기 전에 끝나 버려 인계할 것이 없다)
            }
            if (m != null)
            {
                m.SkillLevels[(int)Skill.Mechanics] = 1f;
                foreach (var c in w.Crew) if (c != m) c.SkillLevels[(int)Skill.Mechanics] = MathF.Min(c.SkillLevels[(int)Skill.Mechanics], 0.8f);
            }
            if (m == null || low == null) Check("인계 — 덜 급한 일을 하는 정비 솜씨 좋은 사람을 찾지 못했다", false);
            else
            {
                var crit = w.Ship.Machines.Where(x => x.Spec.Critical && x.Faults.Count == 0 && x.Spec.Skill == Skill.Mechanics && x.Body.Room != m.Room).OrderBy(x => x.Body.Id).First();
                var fk = crit.Spec.FaultKinds.OrderBy(k => Faults.Spec(k).Part is ItemKind pk && w.Ship.CountStored(pk) == 0 ? 1 : 0).ThenBy(k => k).First(); // 지금 고칠 수 있는 고장 (부품이 있거나 필요 없는)
                w.Machines.Break(crit, fk);
                WorkOrder? urgent = null;
                for (int i = 0; i < 10 && urgent == null; i++) { Run(w, SimTime.Minutes(1)); urgent = w.Board.Open.FirstOrDefault(o => o.Furniture == crit.Body && !o.Closed); }
                if (urgent != null) { urgent.Urgency = MathF.Max(urgent.Urgency, 0.95f); urgent.MinSkill = m.SkillLevel(Skill.Mechanics) - 0.01f; urgent.Assignee = null; }
                int p0 = co.Preempts;
                Until(w, () => urgent?.Assignee == m && (co.Handover(low.Id) != null || co.HandoverUsed > 0), 1.5f);
                string? note = co.Handover(low.Id);
                Check("우선 사용 · 인계 — 핵심 설비 수리에 맞는 손이 덜 급한 일을 하던 사람뿐이면 그가 급한 쪽으로 가고 (스스로 · 컴퓨터가 불러서), 하던 일엔 어디까지 했는지 메모가 남는다",
                    urgent?.Assignee == m && (note != null || co.HandoverUsed > 0),
                    $"{m.Name}: {low.Title}({low.Progress * 100:0}%) → {urgent?.Title} · 맡은 사람 {urgent?.Assignee?.Name} · 보류 {urgent?.BlockedReason} · 부름 {co.Preempts - p0} · 메모 {note ?? (co.HandoverUsed > 0 ? "읽힘" : "없음")} · 지금 {m.Name}: {m.Job?.Label}");
            }
        }

        // 5) 통로: 부상자를 업고 의무실로 가는 길의 문 수리는 지나간 뒤에
        {
            var w = DayOne(seed, "Hanbit");
            var co = w.Automation.Coord;
            var med = w.Ship.RoomsOf(RoomType.Medbay).First();
            var far = w.Ship.LiveRooms.Where(r => r.Type is RoomType.Workshop or RoomType.Storage or RoomType.Quarters && r != med).OrderByDescending(r => (r.Center - med.Center).LengthSquared()).First();
            var victim = Able(w).Last();
            victim.Position = far.Center; victim.PreviousPosition = far.Center; victim.Room = far;
            victim.Vitals.Oxygen = 0.1f; victim.Down = true; victim.Pose = Pose.Down;
            Door? door = null;
            for (int i = 0; i < 180 && door == null; i++)
            {
                Run(w, SimTime.Minutes(1));
                if (victim.CarriedBy is CrewMember carrier && carrier.Room != null)
                {
                    // 업은 사람이 지나갈 길의 문 하나 — 모터가 나갔다 (문 수리가 올라온다)
                    door = w.Ship.Doors.Where(d => !d.Removed && !d.IsExternal && !d.Welded && (d.RoomA == med || d.RoomB == med)).OrderBy(d => d.Id).FirstOrDefault();
                    if (door != null) { door.MotorBroken = true; w.Board.RequestScan(); }
                }
            }
            int pw0 = co.PassageWaits;
            WorkOrder? rd = null;
            for (int i = 0; i < 30 && victim.CarriedBy != null; i++)
            {
                Run(w, SimTime.Minutes(1));
                rd ??= w.Board.Open.FirstOrDefault(o => o.Kind == WorkKind.RepairDoor && o.Target.Door == door);
                if (rd != null && rd.BlockedReason?.Contains("이송") == true) break;
            }
            Check("통로 — 부상자를 업고 의무실로 가는 길의 문 수리(문을 막는 일)는 이송이 지나간 뒤에 한다",
                door != null && rd != null && co.PassageWaits > pw0 && rd.BlockedReason?.Contains("이송") == true,
                $"업음 {victim.CarriedBy?.Name ?? "—"} · 문 {(door != null ? $"{door.RoomA?.Name}↔{door.RoomB?.Name}" : "-")} · 문 수리 {(rd == null ? "없음" : rd.BlockedReason ?? "열림")} · 통로 기다림 {co.PassageWaits - pw0}");
        }

        // 6) 지친 잠: 기력이 바닥나 잠든 사람은 치료 일로 깨우지 않는다 (깨어 성한 손이 있으면 그 손이 간다)
        {
            var w = DayOne(seed, "Hanbit");
            var med = Able(w).OrderByDescending(c => c.SkillLevel(Skill.Medicine)).ThenBy(c => c.Id).First();
            Until(w, () => med.Pose == Pose.Sleeping && med.Job?.Activity is SleepActivity, 30f);
            bool slept = med.Pose == Pose.Sleeping;
            med.Needs.Rest = 0.05f;
            var pat = Able(w).Where(c => c != med && c.IsAwake && c.CanAct).OrderBy(c => c.Id).First();
            NeedsSystem.AddInjury(pat.Vitals, 0.5f, "작업 중 넘어짐");
            bool woke = false, treated = false;
            string? by = null;
            for (int i = 0; i < 90; i++)
            {
                bool spent = med.Needs.Rest < 0.08f && med.Pose == Pose.Sleeping; // 자면서 기력이 차오르면 (8% 넘게) 깨워도 된다
                Run(w, SimTime.Minutes(1));
                if (spent && med.Job?.Order is WorkOrder mo && mo.Kind == WorkKind.Treat) woke = true;
                if (w.Board.Open.FirstOrDefault(o => o.Kind == WorkKind.Treat && o.Target.Crew == pat && !o.Closed) is { Assignee: CrewMember a }) by ??= a.Name;
                if (pat.Vitals.TreatedTick > w.Tick - SimTime.Minutes(2)) treated = true;
            }
            int awake = w.Crew.Count(x => x != med && x.CanAct && x.Pose != Pose.Sleeping && x.Needs.Rest >= 0.25f);
            Check("지친 잠 — 기력이 바닥나 잠든 사람은 치료 일로 깨우지 않는다 (깨어 있는 성한 손이 맡는다 — 깨우면 곧 쓰러져 또 하나의 치료 일이 된다)",
                slept && !woke && awake > 0,
                $"{med.Name}(의료 {med.SkillLevel(Skill.Medicine):0.##}) 기력 5%에 잠듦 · 깨움 {woke} · 지금 {med.Job?.Label} · 다친 {pat.Name} 치료 {(by != null ? $"{by}이 맡음" : treated ? "받음" : "기다림")} · 깨어 성한 손 {awake}");
        }

        Console.WriteLine(_fails == 0 ? "\n✔ 계획 조율" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }

    /// <summary>--coordprobe: 평소 항해에서 조율판이 얼마나 끼어드나 (배터리 · 감출력 · 부품 · 사람 · 통로 · 인계).</summary>
    private static int RunCoordProbe(int seed, int days, string? ship)
    {
        var w = World.CreateDefault(seed, 0, ship ?? "Saeteo");
        var co = w.Automation.Coord;
        for (int d = 1; d <= days; d++)
        {
            Run(w, SimTime.TicksPerDay);
            Console.WriteLine($"  {d}일째 · 감출력 버팀 {co.DerateOk} · 배터리로 {co.DerateCovered} · 미룸 {co.DerateWaits} · 드론 충전 미룸 {co.ChargeHolds} · 불러들임 {co.Recalls} · 부품 양보 {co.PartWaits} · 반환 {co.PartReturns} · 사람 부름 {co.Preempts} · 인계 메모 {co.Notes} (읽힘 {co.HandoverUsed}) · 통로 기다림 {co.PassageWaits}");
        }
        foreach (var x in co.Decisions.TakeLast(10)) Console.WriteLine($"    {SimTime.Clock(x.tick)} {x.text}");
        return 0;
    }
}
