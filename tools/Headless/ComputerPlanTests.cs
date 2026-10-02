using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// v16.26 주컴퓨터 — 예측 · 여러 단계 계획 · 실행 · 확인 · 고침: PLAN v16.26 "확인" 칸을 하나씩 헤드리스로 일으켜 본다
public static partial class Program
{
    private static bool Sec(int k)
    {
        var only = Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith("--only="));
        return only == null || only[7..].Split(',').Contains(k.ToString());
    }

    private static bool PlanDebug => Environment.GetCommandLineArgs().Contains("--plandebug");

    private static void PrintPlan(World w, FixPlan p)
    {
        Console.WriteLine($"    [{p.Problem}] {p.Goal} · {p.Name} · {p.State} · 걸음 {p.Cur}/{p.Steps.Count} · 지금 냉각 {w.Power.EffectiveCooling:0.0} 출력 {w.Power.ReactorOutput:0.0} 노심 {w.Power.ReactorTemperature:0} 상한 {w.Automation.ReactorCap:0.00} 배관망 {w.Piping.Built} 펌프효율 {string.Join("/", w.Ship.FurnitureOf(FurnitureType.CoolantPump).Select(f => f.Machine!.Efficiency.ToString("0.00")))}");
        foreach (var o in p.Compared) Console.WriteLine($"      · {o.Key} {o.Name} {(o.Allowed ? "" : "(" + o.Blocked + ")")} {o.Min:0}~{o.Max:0}분 노심 {o.PeakMin:0}~{o.PeakMax:0} 배터리 {o.BatteryKwh:0.#} 못냄 {o.LostKwh:0.#} 위험 {o.Risk:0.00} 점수 {o.Score:0.0}");
        foreach (var s in p.Steps) Console.WriteLine($"      - {s.Name} {s.State} {s.Min:0}~{s.Max:0} {s.Took(w.Tick):0}분 {s.Note} {s.Waiting}");
        foreach (var (t, why) in p.Revisions) Console.WriteLine($"      ! {SimTime.Clock(t)} {why}");
    }

    private static int RunComputerPlanTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"주컴퓨터 — 여러 단계 계획 · 확인할 방법 · 예약 · 구획 자율 · 자기 진단 · 검토 · 성격 셋 (v16.26) · 시드 {seed}\n");

        // ── 1) 냉각 펌프 고장: 수순을 견주고 세워 실행한다 · 정비사가 늦으면 다시 짠다 ──
        if (Sec(1))
        {
            var w = DayOne(seed, "Hanbit");
            var a = w.Automation;
            var pumps = w.Ship.FurnitureOf(FurnitureType.CoolantPump).ToList();
            var pump = pumps[0];
            // 냉각이 빠듯하게: 다른 분기 방열판이 닳아 있다 — 고장 펌프 몫이 빠지면 노심이 달아오른다
            foreach (var b in w.Piping.Branches) if (b.Pump != pump) b.RadiatorCondition = 0.72f;
            foreach (var p in pumps) if (p != pump) p.Machine!.Condition = 0.55f; // 다른 펌프도 닳아 있다
            if (w.Ship.CountStored(ItemKind.Pump) < 1) foreach (var f in w.Ship.Containers) { if (f.Storage!.Add(ItemKind.Pump, 1) > 0) break; }
            w.Machines.Break(pump.Machine!, FaultKind.PumpSeized);
            Console.WriteLine($"    펌프 {pumps.Count}대 · 냉각 {w.Power.EffectiveCooling:0}kW · 출력 {w.Power.ReactorOutput:0}kW · 한도 {w.Power.ReactorLimit:0} · 노심 {w.Power.ReactorTemperature:0}℃ · 펌프 부품 {w.Ship.CountStored(ItemKind.Pump)}");
            FixPlan? plan = null;
            for (int i = 0; i < SimTime.Minutes(4) && plan == null; i++) { w.Step(); plan = a.Recovery.Plans.FirstOrDefault(p => p.Problem == "냉각"); }
            Check("냉각 펌프 고장 → 수순 계획을 세운다", plan != null, plan == null ? "계획 없음" : $"{plan.Name} · {string.Join(" → ", plan.Steps.Select(s => s.Name))}");
            if (plan != null)
            {
                if (PlanDebug) PrintPlan(w, plan);
                Check("수순을 셋 넘게 견줬다 (한 수가 아니라 행동의 조합)", plan.Compared.Count >= 3 && plan.Compared.All(o => o.Steps.Count >= 2), string.Join(" / ", plan.Compared.Select(o => $"{o.Name}({o.Steps.Count}걸음)")));
                Check("계획마다 성공 · 중단 조건 · 필요한 자원 · 대안 · 뒤따르는 문제 · 범위", plan.Success != "" && plan.Abort != "" && plan.Needs != "" && plan.Fallback != "" && plan.After != "" && plan.Max > plan.Min, $"{plan.Range} · 대안 {plan.Fallback} · {plan.After}");
                var names = plan.Steps.Select(s => s.Name).ToList();
                bool full = names.Contains("예비 펌프로 시간 벌기") && names.Any(n => n.StartsWith("출력")) && names.Contains("수리") && names.Contains("시험 운전") && names.Contains("차례로 정상화");
                Check("예비 펌프로 시간 벌기 → 감출력 → 수리 → 시험 운전 → 차례로 정상화", full || plan.OptionKey == "fix" && plan.PeakMax < FixSteps.ScramC(w) - 30f, string.Join(" → ", names));
                // 정비사가 늦는다: 수리를 맡아 손대기 시작한 사람이 다쳐 손을 놓는다
                int asked = -1;
                for (int i = 0; i < SimTime.Hours(1) && asked < 0; i++)
                {
                    w.Step();
                    var rs = plan.Steps.FirstOrDefault(s => s.Name == "수리");
                    if (rs?.State == FixState.Run && w.Board.All.FirstOrDefault(o => o.Id == rs.Order) is WorkOrder ro && ro.Assignee is CrewMember on && on.Job?.Order == ro && rs.Took(w.Tick) >= 3f) asked = on.Id;
                    if (!plan.Open) break;
                }
                var late = w.Crew.FirstOrDefault(c => c.Id == asked);
                if (late != null) Scenarios.Injure(w, late, 0.1f, 0.6f, "가는 길에 넘어졌다"); // 부탁받은 정비사가 오다가 다쳤다
                int lateRevs = 0;
                FixPlan cur = plan;
                for (int i = 0; i < SimTime.Hours(10); i++)
                {
                    w.Step();
                    if (i % 25 == 0)
                    {
                        cur = a.Recovery.Plans.LastOrDefault(p => p.Problem == "냉각") ?? cur;
                        lateRevs = cur.Revisions.Count(r => r.why.Contains("늦") || r.why.Contains("이어받"));
                        if (!cur.Open && cur.State != "넘김") break;
                    }
                }
                if (PlanDebug) PrintPlan(w, cur);
                Check("정비사가 늦자 다른 사람에게 넘기고 다시 짠다", late == null || lateRevs > 0 && cur.Steps.FirstOrDefault(s => s.Name == "수리")?.Crew != asked, late == null ? "부탁한 사람 없음" : $"늦은 사람 {late.Name} · 수정 {lateRevs}번 · {cur.Revisions.LastOrDefault().why}");
                Check("완료 = 기능이 돌아와 버팀 (수리 → 시험 운전 → 재고장 감시까지)", cur.State == "성공" && cur.Steps.Any(s => s.Name == "시험 운전" && s.State == FixState.Done) && cur.Steps.Any(s => s.Name == "재고장 감시" && s.State == FixState.Done),
                    $"{cur.State} · {string.Join(" → ", cur.Steps.Select(s => $"{s.Name}:{s.State}"))}");
                Check("감출력 · 펌프 세기를 다 되돌렸다 · 긴급 정지 없음", a.Recovery.ReactorCap >= 0.999f && !a.Recovery.Boosting && w.Power.ReactorOnline, $"상한 {a.Recovery.ReactorCap:0.00} · 원자로 {(w.Power.ReactorOnline ? "켜짐" : "꺼짐")}");
                Check("사고 뒤 검토가 예상과 실제를 대조했다", a.Review.Reviews.Any(r => r.PlanId == cur.Id && r.Lines.Any(l => l.Contains("예상"))), a.Review.Reviews.LastOrDefault()?.Lines.FirstOrDefault() ?? "");
                var rd = ComputerReadout.Read(w);
                Check("사람별 작업 시간을 배웠다", a.Review.Values.PaceSamples.Count > 0, string.Join(" · ", a.Review.Values.Pace.Select(kv => $"{w.Crew.First(c => c.Id == kv.Key).Name} {kv.Value:0.00}")));
            }
        }

        // ── 2) 유량이 줄면 원인 셋을 의심 → 예비 센서 대조 → 짧은 시험 운전으로 가린다 ──
        if (Sec(2))
        {
            var w = DayOne(seed, "Hanbit");
            var a = w.Automation;
            var pump = w.Ship.FurnitureOf(FurnitureType.CoolantPump).First();
            var m = pump.Machine!;
            m.Wear = 0.7f; m.Condition = 0.2f; // 실제 원인: 펌프가 닳았다
            ProbeCase? pc = null;
            for (int i = 0; i < SimTime.Minutes(40); i++) { w.Step(); pc ??= a.Probe.Cases.FirstOrDefault(c => c.Furn == pump.Id); if (pc != null && pc.State != "확인 중") break; }
            Check("유량이 줄자 원인 셋을 함께 의심한다", pc != null && pc.H.Count == 3, pc == null ? $"사례 없음 (읽은 유량 {a.Probe.Reading(pump) * 100:0}%)" : string.Join(" · ", pc.H.Select(h => h.Name)));
            if (pc != null)
            {
                var tries = pc.Tries.Where(t => !t.Held).Select(t => t.Name).ToList();
                Console.WriteLine($"    확인: {string.Join(" → ", pc.Tries.Select(t => $"{t.Name}{(t.Held ? "(보류)" : "")}: {t.Result}"))} · 결론 {pc.Conclusion} · 실제 {pc.Truth}");
                Check("가장 싼 확인부터: 예비 센서 대조 → 짧은 시험 운전", tries.Count >= 2 && tries[0] == "예비 센서 대조" && tries[1] == "짧은 시험 운전", string.Join(" → ", tries));
                Check("위험한 확인(펌프 멈추기)은 보류한다", pc.Tries.Any(t => t.Held && t.Key == "stop"), string.Join(" · ", pc.Tries.Where(t => t.Held).Select(t => t.Result)));
                Check("가려낸 원인 = 펌프가 닳았다", pc.Conclusion == "pump", $"{ComputerProbe.Name(pc.Conclusion)} ({pc.Lead.P * 100:0}%)");
            }
            // 틀리면 그 진단의 신뢰를 깎는다
            var w2 = DayOne(seed, "Hanbit");
            var pr = w2.Automation.Probe;
            float before = pr.Trust["meter"];
            var pump2 = w2.Ship.FurnitureOf(FurnitureType.CoolantPump).First();
            pump2.Machine!.SensorCal = 0.5f; // 실제 원인: 유량계
            for (int i = 0; i < SimTime.Minutes(40); i++) w2.Step();
            var c2 = pr.Cases.FirstOrDefault();
            if (c2 != null) Console.WriteLine($"    확인: {string.Join(" → ", c2.Tries.Select(t => $"{t.Name}{(t.Held ? "(보류)" : "")}: {t.Result}"))} · {c2.State} · 결론 {c2.Conclusion} · 실제 {c2.Truth} · {string.Join(" ", c2.H.Select(h => $"{h.Key}{h.P:0.00}"))}");
            Check("유량계가 틀어졌으면 계기 쪽으로 가린다 (예비 센서 값으로 바꿔 읽음)", c2 != null && c2.Conclusion == "meter" && pr.Reading(pump2) > 0.8f * pr.Expected(pump2), c2 == null ? "사례 없음" : $"{c2.Conclusion} · 지금 읽는 유량 {pr.Reading(pump2) * 100:0}%");
        }

        // ── 3) 문 닫기 명령 뒤 센서가 열림이면 다른 길 ──
        if (Sec(3))
        {
            var w = DayOne(seed, "Hanbit");
            var a = w.Automation;
            var room = w.Ship.RoomsOf(RoomType.Hydroponics).First();
            var door = room.Doors.First(d => !d.IsExternal);
            door.Bent = 0.9f; // 문틀이 휘어 끝까지 안 닫힌다
            door.JammedOpen = true;
            Scenarios.Apply(w, "meteor", out _);
            FixPlan? dp = null;
            for (int i = 0; i < SimTime.Minutes(20) && dp == null; i++) { w.Step(); dp = a.Recovery.Plans.FirstOrDefault(p => p.Problem == "문"); }
            if (dp != null && PlanDebug) PrintPlan(w, dp);
            Check("닫으라 했는데 문 센서가 열림 → 명령 성공을 완료로 치지 않는다", dp != null && dp.Steps.Count > 2 && dp.Steps[1].State == FixState.Failed, dp == null ? $"계획 없음 (잠김 {room.Lockdown} · 문 {door.Locked}/{door.Openness:0.00})" : dp.Steps[1].Note);
            Check("다른 길을 탄다 (한 겹 뒤 문 · 손으로 닫기)", dp != null && dp.Steps.Skip(2).Any(s => s.Name is "한 겹 뒤 문 닫기" or "손으로 닫기"), dp == null ? "" : $"{dp.Name}: {string.Join(" → ", dp.Steps.Skip(2).Select(s => s.Name))}");
        }

        // ── 4) 중앙과 끊긴 동안 냉각실 구역 제어기가 버티고, 다시 이어지면 상태부터 대조한다 ──
        if (Sec(4))
        {
            var w = DayOne(seed, "Hanbit");
            var a = w.Automation;
            var cool = w.Ship.RoomsOf(RoomType.Cooling).First();
            var links = w.Net.Links.Where(l => l.Kind == NetKind.Data && (l.Room == cool || l.Door != null && (l.Door.RoomA == cool || l.Door.RoomB == cool))).ToList();
            foreach (var l in links) w.Net.Hurt(l, 1f, "시험");
            for (int i = 0; i < 60 && cool.DataLinked; i++) w.Step();
            bool alone = false;
            for (int i = 0; i < 40 && !alone; i++) { w.Step(); alone = a.Zones.IsAlone(cool); }
            Check("데이터선이 끊기면 그 구획을 모른다고 안다 (구역 제어기가 마지막 방침으로)", alone, $"데이터선 {cool.DataLinked} · 끊긴 고리 {links.Count}");
            var spot = cool.Cells.First(w.Ship.IsOpenFloor);
            w.Fire.Ignite(spot, 0.7f);
            for (int i = 0; i < 45; i++) w.Step();
            var z = a.Zones.Of(cool);
            Check("끊긴 동안 구역 제어기가 불을 보고 환기를 막는다", z != null && z.Did.Contains("vent") && !cool.VentOpen, z == null ? "자율 없음" : string.Join(" · ", z.Journal.Select(j => j.what)));
            foreach (var l in links) l.Integrity = 1f;
            int before = a.Zones.Reconnects;
            for (int i = 0; i < 90 && a.Zones.Reconnects == before; i++) w.Step();
            var rep = a.Zones.Reports.LastOrDefault();
            Check("다시 이어지면 실제 상태부터 대조하고 차이를 남긴다", a.Zones.Reconnects > before && a.Zones.Diffs > 0 && rep.text.Contains("대조"), rep.text ?? "");
            Check("현지가 한 일은 다시 보내지 않는다 (명령 중복 없이 넘겨받음)", a.Zones.DupesAvoided > 0, $"넘겨받은 일 {a.Zones.DupesAvoided}");
        }

        // ── 5) 배터리 예측이 틀린 뒤 다음 예측은 이 배의 실제 용량을 쓴다 ──
        if (Sec(5))
        {
            var w = DayOne(seed, "Hanbit");
            var a = w.Automation;
            foreach (var b in w.Ship.FurnitureOf(FurnitureType.Battery)) b.Machine!.Condition = 0.15f; // 낡은 배터리
            foreach (var p in w.Ship.FurnitureOf(FurnitureType.CoolantPump)) w.Machines.Break(p.Machine!, FaultKind.PumpSeized); // 냉각을 잃어 원자로가 선다
            for (int i = 0; i < SimTime.Hours(9) && a.Review.Values.BatterySamples == 0; i++) w.Step();
            float factor = a.Review.Values.BatteryFactor;
            float real = w.Power.BatteryCapacity / MathF.Max(1f, a.Review.Nameplate());
            Console.WriteLine($"    배터리: {a.Review.BatteryLine} · 실제/이름표 {real:0.00}");
            Check("배터리 예측이 틀렸다 (이름표를 믿었다)", a.Review.LastPredMin > 0f && MathF.Abs(a.Review.LastActualMin - a.Review.LastPredMin) > 0.2f * a.Review.LastPredMin, $"{a.Review.LastPredMin:0}분 예상 → {a.Review.LastActualMin:0}분");
            Check("다음 예측은 이 배의 실제 용량을 쓴다", a.Review.Values.BatterySamples > 0 && MathF.Abs(a.Review.BatteryBelief().cap - w.Power.BatteryCapacity) < 0.25f * w.Power.BatteryCapacity,
                $"믿는 용량 {a.Review.BatteryBelief().cap:0}kWh · 실제 {w.Power.BatteryCapacity:0}kWh (비율 {factor:0.00})");
            var rd = ComputerReadout.Read(w);
            Check("원자로가 서면 재기동 여유를 남기며 운영한다 (자원 예약)", a.Reserve.KeepKwh > 0f && rd.Reserve.Contains("재기동"), rd.Reserve);
        }

        // ── 6) 운 좋게 성공한 위험한 선택을 정답으로 배우지 않는다 ──
        if (Sec(6))
        {
            var w = DayOne(seed, "Hanbit");
            var a = w.Automation;
            float floor = FixSteps.SafetyFloorC;
            float pref0 = a.Review.Prefer("냉각", "fix");
            for (int k = 0; k < 3; k++)
            {
                var risky = new FixPlan { Id = 900 + k, Tick = w.Tick - SimTime.Minutes(40), Problem = "냉각", Key = "냉각:시험", Goal = "시험 — 냉각", Name = "고장 펌프만 고친다", OptionKey = "fix", Min = 30, Max = 50, Risk = 0.6f, PeakMin = 350, PeakMax = 385 };
                a.Recovery.Plans.Add(risky);
                a.Recovery.Finish(risky, "성공");
            }
            var seqs = a.Review.Values.Sequences.GetValueOrDefault("냉각:fix");
            Check("위험했던 수순의 성공은 '운'으로 세고 정답으로 배우지 않는다", seqs.ok == 0 && seqs.lucky == 3 && a.Review.Prefer("냉각", "fix") >= pref0, $"통함 {seqs.ok} · 운 {seqs.lucky} · 선호 {pref0:0.00} → {a.Review.Prefer("냉각", "fix"):0.00}");
            for (int k = 0; k < 3; k++)
            {
                var safe = new FixPlan { Id = 950 + k, Tick = w.Tick - SimTime.Minutes(40), Problem = "냉각", Key = "냉각:시험2", Goal = "시험 — 냉각", Name = "예비 펌프로 버티며 고친다", OptionKey = "boost", Min = 30, Max = 50, Risk = 0.05f, PeakMin = 320, PeakMax = 330 };
                a.Recovery.Plans.Add(safe);
                a.Recovery.Finish(safe, "성공");
            }
            Check("여러 번 안정적으로 통한 수순만 조금 앞세운다 · 안전 제한은 그대로", a.Review.Prefer("냉각", "boost") < 0f && FixSteps.SafetyFloorC == floor, $"boost 선호 {a.Review.Prefer("냉각", "boost"):0.00} · 안전 제한 {FixSteps.SafetyFloorC}℃");
        }

        // ── 7) 같은 명령을 무한 반복하지 않는다: 같은 차단기를 거듭 올리면 스스로 멈추고 사람에게 ──
        if (Sec(7))
        {
            var w = DayOne(seed, "Hanbit");
            var a = w.Automation;
            var panel = w.Ship.FurnitureOf(FurnitureType.PowerPanel).First().Machine!;
            int resets0 = a.Triage.RemoteResets;
            for (int k = 0; k < 6; k++)
            {
                panel.Faults.Add(new Fault { Kind = FaultKind.BreakerTrip, Since = w.Tick, Circuit = 3 });
                for (int i = 0; i < SimTime.Minutes(31); i++) w.Step();
                if (a.SelfWatch.Stops > 0 && !a.SelfWatch.Allow(CmdTarget.Breaker, 3, "차단기 올림")) break;
            }
            int resets = a.Triage.RemoteResets - resets0;
            var bc = a.Triage.Cases.LastOrDefault(c => c.Circuit == 3);
            Check("같은 차단기를 거듭 올리다 스스로 멈춘다", a.SelfWatch.Stops > 0 && !a.SelfWatch.Allow(CmdTarget.Breaker, 3, "차단기 올림"), $"원격으로 올림 {resets}번 · {a.SelfWatch.Notes.LastOrDefault().text}");
            panel.Faults.Add(new Fault { Kind = FaultKind.BreakerTrip, Since = w.Tick, Circuit = 3 });
            for (int i = 0; i < SimTime.Minutes(3); i++) w.Step();
            bc = a.Triage.Cases.LastOrDefault(c => c.Circuit == 3);
            Check("멈춘 뒤엔 원격으로 안 올리고 사람에게 원인을 넘긴다", a.Triage.RemoteResets - resets0 == resets && (bc?.State == "멈춤" || !panel.Faults.Any(f => f.Kind == FaultKind.BreakerTrip && f.Circuit == 3)), $"상태 {bc?.State} · 일감 {w.Board.Open.Count(o => o.Kind == WorkKind.ResetBreaker)}");
        }

        // ── 8) 이송 중 부상자 → 의무실 전력을 남긴다 ──
        if (Sec(8))
        {
            var w = DayOne(seed, "Hanbit");
            var a = w.Automation;
            Scenarios.Apply(w, "downed", out _);
            for (int i = 0; i < SimTime.Minutes(5); i++) w.Step();
            var med = w.Ship.FurnitureOf(FurnitureType.MedBed).FirstOrDefault();
            Check("쓰러진 사람이 의무실로 오는 동안 의무실 전력을 예약한다", a.Reserve.MedHold && med != null && a.Reserve.Holds(med.Machine!) && PowerTriage.Rank(w, med.Machine!) >= 10,
                $"{a.Reserve.Line} · 치료 침대 순위 {(med != null ? PowerTriage.Rank(w, med.Machine!) : -1)}");
        }

        // ── 9) 신입 · 숙련자 · 지친 사람 · 의심 많은 사람에게 다르게 설명한다 · 설비 별명을 알아듣는다 ──
        if (Sec(9))
        {
            var w = DayOne(seed, "Hanbit");
            var a = w.Automation;
            var pump = w.Ship.FurnitureOf(FurnitureType.CoolantPump).First();
            w.Machines.Break(pump.Machine!, FaultKind.PumpSeized);
            FixPlan? p = null;
            for (int i = 0; i < SimTime.Minutes(4) && p == null; i++) { w.Step(); p = a.Recovery.Plans.FirstOrDefault(x => x.Problem == "냉각"); }
            var live = w.Crew.Where(c => !c.Dead && !c.IsChild).ToList();
            var expert = live.OrderByDescending(c => c.SkillLevel(Skill.Mechanics)).First();
            var novice = live.OrderBy(c => c.SkillLevel(Skill.Mechanics)).First(c => c != expert);
            expert.Needs.Rest = 1f; novice.Needs.Rest = 1f;
            if (p != null)
            {
                var step = p.Steps.First(s => s.Name == "수리");
                a.Manner.Learn(expert, FurnitureType.CoolantPump);
                string e = a.Manner.Brief(expert, pump.Machine, p, step), n = a.Manner.Brief(novice, pump.Machine, p, step);
                Console.WriteLine($"    숙련자 {expert.Name}({expert.SkillLevel(Skill.Mechanics):0.00}): {e}\n    신입 {novice.Name}({novice.SkillLevel(Skill.Mechanics):0.00}): {n}");
                Check("숙련자에겐 핵심 수치 · 신입에겐 순서와 이유", e != n && (e.Contains("kW") || a.Manner.StyleFor(expert, Skill.Mechanics) != "수치만") && n.Contains("1)") && n.Contains("까닭"), $"{a.Manner.StyleFor(expert, Skill.Mechanics)} / {a.Manner.StyleFor(novice, Skill.Mechanics)}");
                Check("기술자가 붙인 설비 별명을 알아듣고 그 사람에게 쓴다", a.Manner.Nick(expert, FurnitureType.CoolantPump).StartsWith("'") && e.Contains(a.Manner.Nick(expert, FurnitureType.CoolantPump)), a.Manner.Nick(expert, FurnitureType.CoolantPump));
                novice.Needs.Rest = 0.2f;
                string t = a.Manner.Brief(novice, pump.Machine, p, step);
                a.Trusts.Change(expert, -0.7f, "시험", quiet: true);
                expert.Needs.Rest = 1f;
                string d = a.Manner.Brief(expert, pump.Machine, p, step);
                Check("지친 사람에겐 짧게 · 자주 의심한 사람에겐 근거와 불확실성부터", t.Length < n.Length && d.StartsWith("근거") && d.Contains("확실하지 않은"), $"{t} | {d}");
            }
        }

        // ── 10) 지휘 탭에서 목표 · 사실 · 추측 · 다음 조건 · 대체 계획이 읽힌다 ──
        if (Sec(10))
        {
            var w = DayOne(seed, "Hanbit");
            var a = w.Automation;
            var pump = w.Ship.FurnitureOf(FurnitureType.CoolantPump).First();
            w.Machines.Break(pump.Machine!, FaultKind.PumpSeized);
            for (int i = 0; i < SimTime.Minutes(8); i++) w.Step();
            var rd = ComputerReadout.Read(w);
            Check("지휘 탭: 목표 · 확인된 사실 · 불확실한 것 · 다음 조건 · 예상과 여유 · 대체 계획", rd.Plan != null && rd.Goal != "" && rd.Facts.Count > 0 && rd.Unknowns.Count + rd.Forecasts.Count > 0 && rd.Next != "" && rd.Expect != "" && rd.Fallback != "",
                $"{rd.Goal} | 사실 {rd.Facts.Count} · 추측 {rd.Unknowns.Count + rd.Forecasts.Count} | {rd.Next} | {rd.Expect} | 대안 {rd.Fallback}");
            var (why, when, wait) = ComputerReadout.WhyOff(w, pump);
            Check("설비를 누르면: 왜 꺼져 있나 / 언제 켜지나 / 무엇을 기다리나", why.Contains("고장") && when != "" && wait != "", $"{why} | {when} | {wait}");
            Check("보고 · 센서 · 예측을 구분한다", rd.Facts.Concat(rd.Unknowns).Concat(rd.Forecasts).Select(l => l.Kind).Distinct().Count() >= 2, string.Join(" · ", rd.Facts.Concat(rd.Forecasts).Select(l => l.Kind).Distinct()));
        }

        // ── 13) 자기 진단 · 기능 재배치: 저장 손상 · 센서망 단절 · 분석 기능 이상 · 예비로 넘어갈 때 명령 중복 없음 ──
        if (Sec(13))
        {
            var w = DayOne(seed, "Hanbit");
            var a = w.Automation;
            // 예비로 넘어가도 명령이 두 번 안 나간다
            var pumps = w.Ship.FurnitureOf(FurnitureType.CoolantPump).ToList();
            foreach (var b in w.Piping.Branches) if (b.Pump != pumps[0]) b.RadiatorCondition = 0.72f;
            foreach (var pp in pumps.Skip(1)) pp.Machine!.Condition = 0.55f;
            w.Machines.Break(pumps[0].Machine!, FaultKind.PumpSeized);
            FixPlan? plan = null;
            for (int i = 0; i < SimTime.Minutes(10) && (plan == null || plan.Cur < 2); i++) { w.Step(); plan ??= a.Recovery.Plans.FirstOrDefault(x => x.Problem == "냉각"); }
            int boosts0 = a.Command.Lines.Count(o => o.What.Contains("세기 125%"));
            a.Reboot("시험 — 본체 재부팅", 6f);
            for (int i = 0; i < SimTime.Minutes(12); i++) w.Step();
            int boosts1 = a.Command.Lines.Count(o => o.What.Contains("세기 125%"));
            Check("본체가 멎어 예비로 넘어가도 보낸 명령은 다시 보내지 않는다", plan != null && boosts1 == boosts0 && a.Recovery.Plans.Count(x => x.Problem == "냉각" && x.Open) <= 1, $"펌프 세기 명령 {boosts0} → {boosts1} · 열린 냉각 계획 {a.Recovery.Plans.Count(x => x.Problem == "냉각" && x.Open)}");
            // 저장 손상: 최근 사건 · 지금 계획 · 방침부터
            var comp = a.Computer!;
            comp.Faults.Add(new Fault { Kind = FaultKind.StorageFault, Since = w.Tick });
            for (int i = 0; i < SimTime.Minutes(3); i++) w.Step();
            Check("저장장치가 상하면 최근 사건 · 지금 계획 · 배운 값부터 옮겨 담는다", a.SelfWatch.StorageSaves > 0 && a.Recovery.Plans.Any(x => x.Open), a.SelfWatch.Notes.LastOrDefault(n => n.text.Contains("저장")).text ?? "");
            comp.Faults.RemoveAll(f => f.Kind == FaultKind.StorageFault);
            // 분석 기능 이상: 예측이 거듭 크게 빗나가면 비교를 떼고 정해진 순서로
            a.Review.MissStreak = 3;
            for (int i = 0; i < SimTime.Minutes(2); i++) w.Step();
            Check("예측이 거듭 빗나가면 수순 비교를 떼고 가장 안전한 정해진 순서로", a.SelfWatch.Simple, a.SelfWatch.Status);
            // 센서망 단절: 확신을 낮추고 순찰을 부탁한다 (사람이 실제로 돌아본다)
            if (w.Sensors.CommsRoom is Room comms) comms.BreakerOff = true; // 통신실 중계도 꺼졌다 (무선으로도 못 본다)
            var rooms = w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor).Take(8).ToList();
            foreach (var l in w.Net.Links.Where(l => l.Kind == NetKind.Data && rooms.Contains(l.Room)).ToList()) w.Net.Hurt(l, 1f, "시험");
            for (int i = 0; i < SimTime.Minutes(8); i++) w.Step();
            w.Board.RequestScan();
            for (int i = 0; i < 20; i++) w.Step();
            bool patrol = w.Board.All.Any(o => !o.Closed && o.Kind == WorkKind.PreventiveCheck && o.Detail.Contains("센서가 안 닿는다"));
            Check("센서가 여럿 안 닿으면 확신을 낮추고 순찰을 부탁한다", a.SelfWatch.Sight < 0.75f && patrol, $"닿는 몫 {a.SelfWatch.Sight * 100:0}% · 순찰 일감 {patrol} · {a.SelfWatch.Status}");
        }

        // ── 11) 결정론 ──
        if (Sec(11))
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint h1 = H(), h2 = H();
            Check("결정론 — 같은 시드면 같은 지문", h1 == h2, $"{h1:x8} / {h2:x8}");
        }

        // ── 12) 성능: 30인 배 하루 ──
        if (Sec(12) && !Environment.GetCommandLineArgs().Contains("--noperf"))
        {
            double Day(bool off)
            {
                FixBook.Off = off;
                var w = World.CreateDefault(seed, 0, "Cheonma");
                Run(w, SimTime.Hours(2));
                var sw = Stopwatch.StartNew();
                Run(w, SimTime.TicksPerDay);
                FixBook.Off = false;
                return sw.Elapsed.TotalSeconds;
            }
            double t0 = Day(true), t1 = Day(false);
            Console.WriteLine($"  성능 (30인 하루): 끔 {t0:0.0}초 · 켬 {t1:0.0}초 ({(t1 / t0 - 1) * 100:+0;-0}%) · 계획 {FixBook.Stopwatch * 1000.0 / Stopwatch.Frequency:0}ms");
            Check("성능 · 30인 배 하루가 10% 넘게 늘지 않는다", t1 < t0 * 1.1, $"{t0:0.0} → {t1:0.0}초");
        }

        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}건");
        return _fails == 0 ? 0 : 1;
    }
}
