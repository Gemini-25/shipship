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
            for (int i = 0; i < SimTime.Minutes(90); i++)
            {
                w.Step(); pc ??= a.Probe.Cases.FirstOrDefault(c => c.Furn == pump.Id); if (pc != null && pc.State != "확인 중") break;
                if (PlanDebug && i % SimTime.Minutes(10) == 0) foreach (var o in w.Board.All.Where(o => o.Kind == WorkKind.PreventiveCheck && o.Target.Room == pump.Room))
                    Console.WriteLine($"      {SimTime.Clock(w.Tick)} 순찰 #{o.Id} 닫힘 {o.Closed} · 맡은 {o.Assignee?.Name ?? "-"} · 막힘 {o.BlockedReason} · 진척 {o.Progress:0.00} · 부탁 {pc?.Now?.Crew} · 그 사람 {o.Assignee?.Job?.Order?.Id}/{o.Assignee?.Job?.Current?.GetType().Name}/{o.Assignee?.Room.Name}/{o.Assignee?.Pose} · 방 {pump.Room.Name}");
            }
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
            // 수경재배실 외벽에 구멍 (공기가 샌다 → 격벽 잠금)
            var hole = w.Ship.Walls.First(kv => kv.Value.IsHull && Hull.InsideRoom(w.Ship, kv.Key) == room).Value;
            hole.Integrity = 0.15f;
            hole.Breach = Hull.BreachOf(hole);
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
            var rooms = w.Ship.LiveRooms.Where(r => r.Type != RoomType.Corridor).Take(14).ToList();
            foreach (var l in w.Net.Links.Where(l => l.Kind == NetKind.Data && (rooms.Contains(l.Room) || l.Door != null && (rooms.Contains(l.Door.RoomA!) || rooms.Contains(l.Door.RoomB!)))).ToList()) w.Net.Hurt(l, 1f, "시험");
            for (int i = 0; i < SimTime.Minutes(8); i++) w.Step();
            w.Board.RequestScan();
            for (int i = 0; i < 20; i++) w.Step();
            bool patrol = w.Board.All.Any(o => !o.Closed && o.Kind == WorkKind.PreventiveCheck && o.Detail.Contains("센서가 안 닿는다"));
            Check("센서가 여럿 안 닿으면 확신을 낮추고 순찰을 부탁한다", a.SelfWatch.Sight < 0.8f && patrol, $"닿는 몫 {a.SelfWatch.Sight * 100:0}% · 끊긴 방 {rooms.Count(r => !r.DataLinked)}/{rooms.Count} · 중계 {ComputerV15.Relay(w)} · 순찰 일감 {patrol} · {a.SelfWatch.Status}");
        }

        // ── 14) 자기 진단 둘: 달아오르면 긴 예측 · 검토를 줄이고 냉각은 그대로 · 식으면 미룬 검토 / 계산이 몰리면 급한 것만 / 켰다 껐다 하면 멈춘다 / 주컴퓨터실이 위험하면 예비로 ──
        if (Sec(14))
        {
            var w = DayOne(seed, "Hanbit");
            var a = w.Automation;
            var croom = a.Computer!.Body.Room;
            var pump = w.Ship.FurnitureOf(FurnitureType.CoolantPump).First();
            if (w.Ship.CountStored(ItemKind.Pump) < 1) foreach (var f in w.Ship.Containers) { if (f.Storage!.Add(ItemKind.Pump, 1) > 0) break; }
            int thin0 = a.Outlook.Thinned, rev0 = a.Review.Reviews.Count;
            bool hot = false, thinOk = false;
            FixPlan? plan = null;
            long cooled = -1;
            for (int i = 0; i < SimTime.Hours(6); i++)
            {
                if (cooled < 0) croom.Air.Temperature = MathF.Max(croom.Air.Temperature, 36.5f);
                w.Step();
                if (a.SelfWatch.Hot) { hot = true; thinOk |= a.SelfWatch.Thin("검토") && a.SelfWatch.Thin("예측") && !a.SelfWatch.Thin("경보") && !a.SelfWatch.Thin("문"); }
                if (i == SimTime.Minutes(20)) w.Machines.Break(pump.Machine!, FaultKind.PumpSeized);
                plan ??= a.Recovery.Plans.FirstOrDefault(p => p.Problem == "냉각");
                if (cooled < 0 && plan != null && !plan.Open) { cooled = w.Tick; croom.Air.Temperature = 22f; }
                if (cooled >= 0 && w.Tick - cooled > SimTime.Minutes(40)) break;
            }
            bool reviewedHot = plan != null && a.Review.Reviews.Any(r => r.PlanId == plan.Id && r.Tick < cooled);
            bool reviewedLater = plan != null && a.Review.Reviews.Any(r => r.PlanId == plan.Id);
            Check("달아오르면 긴 예측 · 사고 검토를 줄이고, 냉각 계획 · 경보 · 문은 그대로 돈다", hot && thinOk && a.Outlook.Thinned > thin0 && plan != null,
                $"달아오름 {hot} · 줄인 예측 {a.Outlook.Thinned - thin0}번 · 냉각 계획 {plan?.Name ?? "없음"} ({plan?.State})");
            Check("달아오른 동안 미룬 사고 검토는 식은 뒤에 한다", plan != null && !plan.Open && !reviewedHot && reviewedLater, $"식은 뒤 검토 {reviewedLater} · 달아오른 때 검토 {reviewedHot} · {a.SelfWatch.Notes.LastOrDefault(n => n.text.Contains("식었다")).text}");

            // 계산이 몰린다: 본체가 상하고 달아올라 연산이 반 아래 → 급한 것(냉각 · 문)만, 다른 설비 계획은 미룬다
            var comp = a.Computer!;
            float wear0 = comp.Wear, cond0 = comp.Condition; comp.Wear = 1f; comp.Condition = 0.3f;
            var o2 = w.Ship.FurnitureOf(FurnitureType.OxygenGenerator).First().Machine!;
            bool crowded = false, o2Plan = false, coolPlan = false;
            var pump2 = w.Ship.FurnitureOf(FurnitureType.CoolantPump).Skip(1).FirstOrDefault() ?? pump;
            if (w.Ship.CountStored(ItemKind.Pump) < 1) foreach (var f in w.Ship.Containers) { if (f.Storage!.Add(ItemKind.Pump, 1) > 0) break; }
            for (int i = 0; i < SimTime.Minutes(12); i++)
            {
                croom.Air.Temperature = 39f;
                w.Step();
                if (i == SimTime.Minutes(1)) // 불이 여러 곳에서 한꺼번에 — 줄일 수 없는 계산이 몰린다
                    foreach (var fr in w.Ship.LiveRooms.Where(x => x != croom && x != pump2.Room && x != o2.Body.Room && x.Type is not (RoomType.Corridor or RoomType.LifeSupport or RoomType.Medbay)).OrderBy(x => x.Id).Take(6))
                        w.Fire.Ignite(fr.Cells.First(w.Ship.IsOpenFloor), 0.6f);
                if (i == SimTime.Minutes(3)) { w.Machines.Break(o2, FaultKind.ElectrolyzerFault); w.Machines.Break(pump2.Machine!, FaultKind.PumpSeized); }
                crowded |= a.SelfWatch.Crowded;
                if (a.SelfWatch.Crowded)
                {
                    o2Plan |= a.Recovery.Plans.Any(p => p.Open && p.TargetId == o2.Body.Id);
                    coolPlan |= a.Recovery.Plans.Any(p => p.Open && p.Problem == "냉각" && p.TargetId == pump2.Id);
                }
            }
            if (PlanDebug) Console.WriteLine($"    용량 {a.Core.Capacity():0.0} · 부하 {a.Load:0.00} · 본체 {a.MainOnline} · 코어 {a.CoreOnline} · 예비 {a.BackupActive} · 원자로 {w.Power.ReactorOnline} · 펌프2 {pump2.Id}/{pump.Id} 고장 {string.Join(",", pump2.Machine!.Faults.Select(f => f.Kind))} · 계획 {string.Join(" / ", a.Recovery.Plans.Select(p => $"{p.Problem}:{p.TargetId}:{p.State}"))}");
            Check("계산이 몰리면 급한 것(냉각)만 하고 다른 설비 계획은 미룬다", crowded && coolPlan && !o2Plan, $"부하 {a.Load * 100:0}% · 몰림 {crowded} · 냉각 계획 {coolPlan} · 산소 발생기 계획 {o2Plan}");
            comp.Wear = wear0; comp.Condition = cond0;
            croom.Air.Temperature = 22f;

            // 켰다 껐다: 같은 설비를 두 시간 안에 네 번 끄고 켜면 스스로 멈추고, 몰아주기도 그 설비를 다시 켜지 않는다
            var toy = w.Ship.Machines.First(m => m.Spec.PowerDraw > 0f && !m.Spec.Critical && m.Body.Type != FurnitureType.MainComputer);
            int toggles0 = a.SelfWatch.Toggles;
            for (int k = 0; k < 4; k++)
            {
                a.Command.Line(CmdTarget.Machine, toy.Body.Id, toy.Body.Room, k % 2 == 0 ? $"{toy.Name} 끔 (시험)" : $"{toy.Name} 다시 켬", "시험", 0.5f, 5f);
                for (int i = 0; i < SimTime.Minutes(5); i++) w.Step();
            }
            Check("켰다 껐다를 되풀이하면 스스로 멈추고 원인을 사람에게 넘긴다", a.SelfWatch.Toggles > toggles0 && !a.SelfWatch.Allow(CmdTarget.Machine, toy.Body.Id, "다시 켬"), a.SelfWatch.Notes.LastOrDefault().text ?? "");

            // 주컴퓨터실이 위험하다: 지금 계획 · 명령 상태를 예비 연산기로 넘긴다
            int moves0 = a.SelfWatch.Relocations;
            for (int i = 0; i < SimTime.Minutes(4) && !a.SelfWatch.Moved; i++) { croom.Air.Temperature = 47f; w.Step(); }
            Check("주컴퓨터실이 위험하면 지금 계획 · 명령 상태를 예비로 넘긴다", a.SelfWatch.Moved && a.SelfWatch.Relocations > moves0 && a.SelfWatch.Thin("예측") && !a.SelfWatch.Thin("경보"), a.SelfWatch.Status);
        }

        // ── 15) 끊긴 동안 미뤄 둔 위험한 원격 명령(시험 운전)은 유효 시간이 지나면 하지 않고 상태부터 다시 본다 ──
        if (Sec(15))
        {
            var w = DayOne(seed, "Hanbit");
            var a = w.Automation;
            var pump = w.Ship.FurnitureOf(FurnitureType.CoolantPump).First();
            if (w.Ship.CountStored(ItemKind.Pump) < 1) foreach (var f in w.Ship.Containers) { if (f.Storage!.Add(ItemKind.Pump, 1) > 0) break; }
            w.Machines.Break(pump.Machine!, FaultKind.PumpSeized);
            FixPlan? plan = null;
            for (int i = 0; i < SimTime.Hours(2); i++)
            {
                w.Step();
                plan ??= a.Recovery.Plans.FirstOrDefault(p => p.Problem == "냉각");
                if (plan?.Step is FixStep rs && rs.Name == "수리" && rs.State == FixState.Run) break;
            }
            var room = pump.Room;
            var links = w.Net.Links.Where(l => l.Kind == NetKind.Data && (l.Room == room || l.Door != null && (l.Door.RoomA == room || l.Door.RoomB == room))).ToList();
            foreach (var l in links) w.Net.Hurt(l, 1f, "시험");
            for (int i = 0; i < 60 && room.DataLinked; i++) w.Step();
            pump.Machine!.Faults.Clear(); // 끊긴 사이 정비사가 고쳤다 (중앙은 아직 모른다)
            bool held = false;
            int cmds0 = a.Command.Lines.Count(o => o.What.Contains("시험 운전"));
            for (int i = 0; i < SimTime.Minutes(14); i++)
            {
                w.Step();
                var cur = plan == null ? null : a.Recovery.Plans.LastOrDefault(p => p.Problem == "냉각" && p.Open);
                held |= cur?.Step is FixStep ts && ts.Name == "시험 운전" && ts.State == FixState.Wait && ts.Waiting.Contains("끊");
            }
            int cmds1 = a.Command.Lines.Count(o => o.What.Contains("시험 운전"));
            int exp0 = a.Zones.Expired, rc0 = a.Zones.Reconnects;
            foreach (var l in links) l.Integrity = 1f;
            for (int i = 0; i < 90 && a.Zones.Reconnects == rc0; i++) w.Step();
            var last = a.Recovery.Plans.LastOrDefault(p => p.Problem == "냉각");
            Check("끊긴 동안 위험한 원격 명령(시험 운전)은 보내지 않고 기다린다", plan != null && held && cmds1 == cmds0, $"기다림 {held} · 시험 운전 명령 {cmds0} → {cmds1} · {plan?.Step?.Waiting}");
            Check("다시 이어졌을 때 유효 시간이 지난 명령은 하지 않고 상태부터 다시 본다", a.Zones.Expired > exp0 && last != null && last.Revisions.Any(r => r.why.Contains("유효 시간")), last?.Revisions.LastOrDefault().why ?? "");
        }

        // ── 16) 자원 예약: 마지막 부품 · 계획이 부를 사람 · 지친 기술자 — 승무원이 실제로 다르게 움직인다 ──
        if (Sec(16))
        {
            var w = DayOne(seed, "Hanbit");
            var a = w.Automation;
            Run(w, SimTime.Hours(31)); // 주컴퓨터가 사람을 알아 가는 데 하루 남짓 걸린다 (쉬라는 부탁은 아는 사람에게만)
            // 정수기 필터가 마지막 하나
            int filters = w.Ship.CountStored(ItemKind.Filter);
            if (filters < 1) foreach (var f in w.Ship.Containers) { if (f.Storage!.Add(ItemKind.Filter, 1) > 0) break; }
            foreach (var f in w.Ship.Containers) { int n = w.Ship.CountStored(ItemKind.Filter); if (n <= 1) break; f.Storage!.Take(ItemKind.Filter, n - 1); }
            var pump = w.Ship.FurnitureOf(FurnitureType.CoolantPump).First();
            if (w.Ship.CountStored(ItemKind.Pump) < 1) foreach (var f in w.Ship.Containers) { if (f.Storage!.Add(ItemKind.Pump, 1) > 0) break; }
            w.Machines.Break(pump.Machine!, FaultKind.PumpSeized);
            FixPlan? plan = null;
            CrewMember? held = null;
            for (int i = 0; i < SimTime.Hours(1) && held == null; i++)
            {
                w.Step();
                plan ??= a.Recovery.Plans.FirstOrDefault(p => p.Problem == "냉각");
                if (plan?.Steps.FirstOrDefault(s => s.Name == "수리") is FixStep rs && rs.Crew >= 0 && a.Reserve.HoldsCrew(w.Crew.First(c => c.Id == rs.Crew))) held = w.Crew.First(c => c.Id == rs.Crew);
            }
            Check("핵심 설비의 마지막 부품은 다음 고장 몫으로 묶어 둔다", w.Ship.CountStored(ItemKind.Filter) != 1 || a.Reserve.Now.Any(r => r.Kind == "부품" && r.What.Contains("마지막")), string.Join(" · ", a.Reserve.Now.Select(r => $"{r.Kind}:{r.What}")));
            var routine = w.Board.Open.FirstOrDefault(o => !o.Closed && CrewModelBook.Routine(o.Kind) && o.Urgency < 0.6f);
            routine ??= w.Board.All.FirstOrDefault(o => CrewModelBook.Routine(o.Kind) && o.Urgency < 0.6f);
            // 통합8 그 순간 게시판에 잡일이 하나도 없는 날이 있다 — 예약이 걸린 동안 잡일이 올라올 때까지 (한 시간 안)
            for (int i = 0; i < SimTime.Hours(1) && routine == null && held != null && a.Reserve.HoldsCrew(held); i++)
            {
                w.Step();
                routine = w.Board.Open.FirstOrDefault(o => !o.Closed && CrewModelBook.Routine(o.Kind) && o.Urgency < 0.6f);
            }
            float bias = held != null && routine != null ? a.CrewModel.RequestBias(held, routine) : 0f;
            Check("계획이 곧 부를 사람은 예약하고 — 그 사람은 늘 하던 잡일을 집지 않는다", held != null && routine != null && bias < 0f, $"{held?.Name ?? "없음"} · {routine?.Title ?? "잡일 없음"} · 치우침 {bias:0.00}");
            // 계획이 도는 동안 지친 기술자에겐 지금 쉬라 한다 (다음 교대 몫) — 그 사람도 잡일을 미룬다
            var tired = w.Crew.Where(c => !c.Dead && c.CanAct && c != held && a.CrewModel.Ready(c) && (c.SkillLevel(Skill.Mechanics) >= 0.5f || c.SkillLevel(Skill.Electrical) >= 0.5f)).OrderBy(c => c.Id).FirstOrDefault();
            if (tired != null) tired.Needs.Rest = 0.12f;
            for (int i = 0; i < SimTime.Minutes(5) && tired != null && !a.CrewModel.RestAsked(tired); i++) { if (tired.Needs.Rest > 0.15f) tired.Needs.Rest = 0.12f; w.Step(); }
            float tb = tired != null && routine != null ? a.CrewModel.RequestBias(tired, routine) : 0f;
            Check("계획이 도는 동안 지친 기술자는 쉬게 해 다음 교대를 남긴다 — 그 사람도 잡일을 미룬다", plan?.Open == true && tired != null && a.CrewModel.RestAsked(tired) && tb < 0f, $"{tired?.Name ?? "없음"} · 쉬라 함 {tired != null && a.CrewModel.RestAsked(tired)} · 치우침 {tb:0.00}");
        }

        // ── 17) 복구 순서: 부품이 오기 전엔 사람을 세우지 않는다 · 한 사람에게 몰리면 나눈다 · 사람이 붙은 설비는 다시 켜지 않는다 ──
        if (Sec(17))
        {
            var w = DayOne(seed, "Hanbit");
            var a = w.Automation;
            var o2 = w.Ship.FurnitureOf(FurnitureType.OxygenGenerator).First().Machine!;
            w.Machines.Break(o2, FaultKind.ElectrolyzerFault);
            var need = o2.Faults.SelectMany(f => f.Materials).Select(x => x.kind).Distinct().ToList();
            var keep = need.ToDictionary(k => k, k => w.Ship.CountStored(k));
            foreach (var k in need) foreach (var f in w.Ship.Containers) f.Storage!.Take(k, 999);
            FixPlan? plan = null;
            for (int i = 0; i < SimTime.Minutes(15); i++) { w.Step(); plan ??= a.Recovery.Plans.FirstOrDefault(p => p.Open && p.TargetId == o2.Body.Id); }
            var part = plan?.Steps.FirstOrDefault();
            var fix = plan?.Steps.FirstOrDefault(s => s.Name == "수리");
            Check("부품이 없으면 '부품 구하기'가 앞에 서고, 오기 전엔 사람을 세우지 않는다", plan != null && part?.Name == "부품 구하기" && part.State == FixState.Run && fix != null && fix.Crew < 0 && part.Waiting.Contains("세우지 않는다"),
                plan == null ? "계획 없음" : $"{string.Join(" → ", plan.Steps.Select(s => $"{s.Name}:{s.State}"))} · {part?.Waiting}");
            foreach (var k in need) { int n = Math.Max(2, keep[k]); foreach (var f in w.Ship.Containers) { n -= f.Storage!.Add(k, n); if (n <= 0) break; } }
            for (int i = 0; i < SimTime.Minutes(6) && (fix?.Crew ?? 0) < 0; i++) w.Step();
            Check("부품이 들어오면 그때 사람을 부른다", fix != null && fix.Crew >= 0, fix == null ? "" : $"{fix.State} · {fix.Note}");
            // 같은 손이 필요한 다른 핵심 설비도 고장 — 그 사람이 이미 붙어 있으면 다른 사람에게 나눈다
            var other = w.Ship.Machines.Where(m => m.Spec.Critical && m != o2 && m.Body.Type is not (FurnitureType.CoolantPump or FurnitureType.MainComputer or FurnitureType.ReactorCore) && m.Faults.Count == 0)
                .OrderBy(m => m.Spec.Skill == o2.Spec.Skill ? 0 : 1).ThenBy(m => m.Body.Id).FirstOrDefault();
            FixPlan? plan2 = null;
            if (other != null)
            {
                var kind = other.Body.Type == FurnitureType.WaterRecycler ? FaultKind.FilterClogged : other.Body.Type == FurnitureType.OxygenGenerator ? FaultKind.ElectrolyzerFault : FaultKind.ControlFault;
                w.Machines.Break(other, kind);
                foreach (var (k, c) in other.Faults.SelectMany(f => f.Materials)) if (w.Ship.CountStored(k) < c) foreach (var f in w.Ship.Containers) { if (f.Storage!.Add(k, c) > 0) break; }
                for (int i = 0; i < SimTime.Minutes(10); i++) { w.Step(); plan2 ??= a.Recovery.Plans.FirstOrDefault(p => p.Open && p.TargetId == other.Body.Id); if (plan2?.Steps.FirstOrDefault(s => s.Name == "수리")?.Crew >= 0) break; }
            }
            var fix2 = plan2?.Steps.FirstOrDefault(s => s.Name == "수리");
            string who(int id) => w.Crew.FirstOrDefault(c => c.Id == id)?.Name ?? "-";
            Check("한 사람에게 수리가 몰리면 다른 사람에게 나눈다", fix != null && fix2 != null && fix.Crew >= 0 && fix2.Crew >= 0 && fix2.Crew != fix.Crew,
                $"{o2.Name}: {who(fix?.Crew ?? -1)} · {other?.Name}: {who(fix2?.Crew ?? -1)} · 나눔 {a.Recovery.Shared} · {plan2?.Revisions.LastOrDefault().why}");
            // 재기동 잠금: 수리가 끝났어도 정비 잠금이 걸려 있으면(사람이 붙어 있으면) 원격으로 시험 운전을 하지 않는다
            if (plan2 != null && other != null)
            {
                other.LockedOut = true;
                other.Faults.Clear();
                int locks0 = a.Recovery.LockWaits;
                int tests0 = a.Command.Lines.Count(o => o.TargetId == other.Body.Id && o.What.Contains("시험 운전"));
                FixStep? test = null;
                for (int i = 0; i < SimTime.Minutes(5); i++) { w.Step(); test = a.Recovery.Plans.LastOrDefault(p => p.TargetId == other.Body.Id)?.Steps.FirstOrDefault(s => s.Name == "시험 운전"); }
                int tests1 = a.Command.Lines.Count(o => o.TargetId == other.Body.Id && o.What.Contains("시험 운전"));
                bool waited = test != null && test.State == FixState.Wait && test.Waiting.Contains("재기동 잠금");
                other.LockedOut = false;
                for (int i = 0; i < SimTime.Minutes(4); i++) w.Step();
                test = a.Recovery.Plans.LastOrDefault(p => p.TargetId == other.Body.Id)?.Steps.FirstOrDefault(s => s.Name == "시험 운전");
                Check("사람이 붙어 일하는 설비는 원격으로 다시 켜지 않고, 잠금이 풀리면 시험 운전한다", waited && tests1 == tests0 && a.Recovery.LockWaits > locks0 && test != null && test.State != FixState.Wait,
                    $"기다림 {waited} · 잠긴 동안 명령 {tests1 - tests0} · 풀린 뒤 {test?.State} {test?.Note}");
            }
        }

        // ── 18) 성격 셋 — 말투 · 판단 선호: 위기엔 말이 짧아진다 · 긴급 정지를 겪으면 여유를 더 남긴다 (안전 제한은 그대로) ──
        if (Sec(18))
        {
            var w = DayOne(seed, "Hanbit");
            var a = w.Automation;
            const string line = "냉각 펌프 하나가 멎어 냉각이 모자랍니다 · 예비 펌프를 세게 돌리고 출력을 낮춥니다 · 정비사를 부릅니다";
            string calm = a.Manner.Speak(line);
            var wr = WreckedShip(seed, "Hanbit");
            for (int i = 0; i < SimTime.Minutes(30) && Crisis.Level(wr) < CrisisLevel.Emergency; i++) wr.Step();
            string tense = wr.Automation.Manner.Speak(line);
            Check("위기엔 말이 짧아진다 (한가할 땐 다 말한다)", calm == line && tense.Length < line.Length, $"평시: {calm} | {Crisis.Name(Crisis.Level(wr))}: {tense}");
            var c = new FixCase { Problem = "냉각", Scram = FixSteps.ScramC(w) };
            FixOption Near() => new FixOption { Key = "near", Name = "아슬아슬", PeakMin = c.Scram - 30f, PeakMax = c.Scram - FixSteps.SafetyFloorC - 2f, Min = 30f, Max = 60f };
            for (int i = 0; i < SimTime.Hours(1) + 10; i++) w.Step();
            var o0 = Near(); FixSteps.Score(w, c, o0);
            float m0 = a.Manner.MarginPref;
            w.History.Scrams++; // 원자로가 긴급 정지했다
            for (int i = 0; i < SimTime.Hours(1) + 10; i++) w.Step();
            var o1 = Near(); FixSteps.Score(w, c, o1);
            Check("긴급 정지를 겪으면 여유를 더 남긴다 — 아슬아슬한 수순의 점수가 나빠진다 · 안전 제한은 그대로", a.Manner.MarginPref > m0 && o1.Score > o0.Score && FixSteps.SafetyFloorC >= 12f,
                $"여유 {m0:0.00} → {a.Manner.MarginPref:0.00} · 점수 {o0.Score:0.0} → {o1.Score:0.0} · {a.Manner.Shifts.LastOrDefault().text}");
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
