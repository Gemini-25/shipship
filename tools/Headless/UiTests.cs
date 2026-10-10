using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v16.2 UI 기반: 승무원 카드의 이유 사슬 · 자원 "언제 문제가 되나" · 기록 묶기 · 화면이 읽기만 하는지(결정론)
public static partial class Program
{
    private static int RunUiTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"UI 기반 점검 (v16.2) · 시드 {seed}\n");
        try
        {
            // 1) 검증 장면: 배고픈 사람 → 식당 → 자리 없음 → 기다림 (식당 자리를 모두 남이 잡아 둔다)
            {
                var w = DayOne(seed, "Hanbit");
                var seats = w.Ship.RoomsOf(RoomType.Mess).SelectMany(r => r.Furniture).Where(f => f.Type == FurnitureType.Seat).ToList();
                var p = w.Crew.Where(c => c.CanAct && c.IsAwake && c.Job?.Activity is not SleepActivity && !c.Outside).OrderBy(c => c.Id).First();
                var other = w.Crew.First(c => c != p);
                p.Needs.Food = 0.12f; // 몹시 배고픔 (굶주림 문턱 아래)
                var seen = new List<string>();
                string? full = null;
                Room? target = null;
                for (int t = 0; t < SimTime.Hours(2) && full == null; t++)
                {
                    foreach (var s in seats) if (s.ReservedBy == null || s.ReservedBy == p) s.ReservedBy = other; // 자리가 없다
                    w.Step();
                    if (p.Job?.Activity is not EatActivity) continue;
                    var chain = CrewWhy.Chain(p, w);
                    string line = string.Join(" → ", chain.Select(x => x.Text));
                    if (seen.Count == 0 || seen[^1] != line) seen.Add(line);
                    target = p.Job.TargetRoom;
                    if (chain[0].Text.Contains("배고픔") && chain.Any(x => x.Kind == WhyKind.Obstacle && x.Text == "자리 없음") && chain[^1].Text == "기다림")
                        full = line;
                }
                Check("이유 사슬 — 배고픈 사람을 누르면 '배고픔 → 식당 → 자리 없음 → 기다림'", full != null && target != null && full.Contains(target.Name),
                    $"{p.Name}: {full ?? "(없음)"} · 거쳐 간 사슬 {string.Join(" | ", seen.Take(5))}");

                // 같은 사람 · 자리가 있을 때는 '자리 없음'이 없다 (사슬은 실제 상황을 읽는다)
                var w2 = DayOne(seed, "Hanbit");
                var p2 = w2.Crew.First(c => c.Id == p.Id);
                p2.Needs.Food = 0.12f;
                bool sawEat = false, sawSeatless = false, sawSeated = false;
                for (int t = 0; t < SimTime.Hours(2); t++)
                {
                    w2.Step();
                    if (p2.Job?.Activity is not EatActivity || p2.Job.Label != "식사") continue;
                    sawEat = true;
                    var chain = CrewWhy.Chain(p2, w2);
                    bool seated = p2.Job.Reservations.Any(f => f.Type == FurnitureType.Seat);
                    if (chain.Any(x => x.Text == "자리 없음")) sawSeatless = true;
                    if (seated) sawSeated = true;
                    if (seated && sawSeatless) break;
                }
                Check("이유 사슬 — 자리가 있으면 '자리 없음'이 붙지 않는다", sawEat && sawSeated && !sawSeatless, $"식사 {sawEat} · 자리 잡음 {sawSeated} · 자리 없음 표시 {sawSeatless}");

                // 승무원 카드의 나머지: 최근 기억 3 · 관계 3 · 지닌 물건
                var mem = CrewWhy.RecentMemories(p, 3);
                var rel = CrewWhy.TopRelations(p, w, 3);
                var things = CrewWhy.Things(p, w);
                bool memSorted = mem.Zip(mem.Skip(1)).All(z => z.First.tick >= z.Second.tick);
                Check("승무원 카드 — 기억 ≤3(최근 순) · 관계 3(가장 강한 사이부터) · 물건 목록", mem.Count <= 3 && memSorted && rel.Count == Math.Min(3, w.Crew.Count(c => c != p && !c.Dead))
                    && rel.Zip(rel.Skip(1)).All(z => MathF.Abs(z.First.value) >= MathF.Abs(z.Second.value)),
                    $"기억 {mem.Count} · 관계 {string.Join(", ", rel.Select(r => $"{r.who.Name} {r.word}"))} · 물건 {things.Count} ({string.Join(" / ", things.Take(3))})");

                // 다른 상황도 사슬이 나온다: 모두에게 빈 사슬이 없다
                int empty = w.Crew.Count(c => CrewWhy.Chain(c, w).Count == 0);
                Check("이유 사슬 — 누구를 눌러도 사슬이 있다 (동기 · 지금 단계)", empty == 0,
                    string.Join(" | ", w.Crew.Take(4).Select(c => $"{c.Name}: {CrewWhy.Line(c, w)}")));
            }

            // 1-b) 여러 시스템에 걸친 영향: 미끄러져 다침 → 부상 → 작업 느림 — 카드에 보이는 배율이 실제 작업 진척과 같다
            {
                var wa = DayOne(seed, "Hanbit");
                var wb = DayOne(seed, "Hanbit");
                CrewMember? pa = null;
                for (int t = 0; t < SimTime.Hours(12) && pa == null; t++)
                {
                    wa.Step(); wb.Step();
                    pa = wa.Crew.FirstOrDefault(c => c.Job?.Current is WorkToil && c.Vitals.Injury < 0.01f && c.Helper == null
                        && (c.Job.Order == null || c.Job.Order.Robot == null && wa.CrisisCrew.HelpersOf(c.Job.Order).Count == 0)); // 통합: 같은 일을 로봇 · 거드는 사람과 나눠 하면 진척이 섞인다 (한 사람의 손 빠르기만 잰다)
                }
                string detail = "일하는 사람을 못 찾음";
                bool ok = false;
                if (pa != null)
                {
                    var pb = wb.Crew.First(c => c.Id == pa.Id);
                    NeedsSystem.AddInjury(pb.Vitals, 0.4f, "미끄러져 허리를 삐었다");
                    float a0 = pa.Job!.Current!.Progress ?? 0f, b0 = pb.Job?.Current?.Progress ?? 0f;
                    wa.Step(); wb.Step();
                    float a1 = pa.Job?.Current?.Progress ?? a0, b1 = pb.Job?.Current?.Progress ?? b0;
                    float ratio = (b1 - b0) / MathF.Max(1e-9f, a1 - a0);
                    var link = CrewWhy.Influences(pb, wb).FirstOrDefault(l => l.Source == WhySource.Body);
                    float expect = (1f - 0.25f * pb.Vitals.Injury) * Wounds.HandFactor(pb.Vitals);
                    string shown = link.Steps == null ? "(없음)" : string.Join(" → ", link.Steps);
                    ok = link.Steps != null && link.Steps[0].Contains("미끄러져") && shown.Contains($"작업 {MathF.Abs(1f - expect) * 100f:0}% 느림") && MathF.Abs(ratio - expect) < 0.02f && link.Hurts;
                    detail = $"{pb.Name}: {shown} · 실제 진척 비 {ratio:0.000} / 카드 {expect:0.000}";
                }
                Check("영향 사슬 — '미끄러져 다침 → 부상 → 작업 n% 느림'이 실제 작업 속도와 같다", ok, detail);
            }

            // 1-c) 원인이 여러 시스템에 걸친 사슬: 젖은 바닥(출처까지) → 미끄러짐 → 부상 → 작업 느림 — 통로를 계속 적셔 실제로 미끄러질 때까지
            {
                var w = DayOne(seed, "Hanbit");
                var wet = w.Ship.Rooms.Where(r => r.Kind == RoomType.Corridor && !r.Detached).SelectMany(r => r.Cells).ToList();
                CrewMember? slipped = null;
                bool staged = false;
                for (int t = 0; t < SimTime.Hours(10) && slipped == null; t++)
                {
                    if (t % 30 == 0) foreach (var cell in wet) w.Body.SetMark(cell, CellMark.Wet, 1f, "배관 누수");
                    w.Step();
                    if (t % 10 != 0) continue;
                    slipped = w.Crew.FirstOrDefault(c => !c.Dead && c.Vitals.InjuryCause == "미끄러져 다침" && c.Vitals.Injury > 0.01f
                        && w.Log.Entries.Skip(Math.Max(0, w.Log.Entries.Count - 400)).Any(e => e.CrewId == c.Id && e.Text.Contains("물기에 미끄러져 넘어졌다")));
                }
                if (slipped == null)
                {
                    // 열 시간 안에 아무도 다치지 않았으면 장면을 꾸민다 (규칙은 그대로: 기록 · 바닥 상태 · 부상)
                    staged = true;
                    slipped = w.Crew.First(c => c.CanAct);
                    var room = w.Ship.Rooms.First(r => r.Kind == RoomType.Corridor);
                    foreach (var cell in room.Cells) w.Body.SetMark(cell, CellMark.Wet, 1f, "배관 누수");
                    w.Log.Add(w.Tick, LogKind.Warning, $"{Ko.IGa(slipped.Name)} 금속판 바닥 물기에 미끄러져 넘어졌다 ({room.Name})", slipped.Id);
                    NeedsSystem.AddInjury(slipped.Vitals, 0.3f, "미끄러져 다침");
                }
                var link = CrewWhy.Influences(slipped, w).FirstOrDefault(l => l.Source == WhySource.Body);
                string shown = link.Steps == null ? "(없음)" : string.Join(" → ", link.Steps);
                float expect = (1f - 0.25f * slipped.Vitals.Injury) * Wounds.HandFactor(slipped.Vitals);
                bool ok = link.Steps != null && link.Steps.Length >= 5 && link.Steps[0] == "배관 누수" && link.Steps[1].EndsWith("젖은 바닥")
                    && link.Steps[2].EndsWith("미끄러짐") && link.Steps[3].StartsWith("부상") && link.Steps[^1].Contains($"작업 {MathF.Abs(1f - expect) * 100f:0}% 느림") && link.Hurts;
                Check("영향 사슬 — '배관 누수 → 젖은 바닥 → 미끄러짐 → 부상 → 작업 n% 느림' (여러 시스템을 잇는다)", ok,
                    $"{slipped.Name}{(staged ? " (꾸민 장면)" : " (실제로 미끄러짐)")}: {shown} · 기대 작업 배율 {expect:0.000}");

                // 정전 사슬: 침수로 분전함을 내리면 → 그 방 정전 → 캄캄함 (그 방 기록을 그대로 읽는다)
                var target = w.Crew.Where(c => c.CanAct && c.Room is Room r && r.Kind != RoomType.Corridor).Select(c => c.Room!).FirstOrDefault()
                             ?? w.Ship.Rooms.First(r => r.Kind != RoomType.Corridor);
                for (int t = 0; t < 120 && (target.Powered || !target.BreakerOff); t++)
                {
                    if (!target.BreakerOff) w.Moisture.Isolate(target, null); // 바닥이 말라 곧 다시 올리면 또 내린다
                    w.Step();
                }
                var lo = CrewWhy.LightOrigin(target, w);
                var inside = w.Crew.FirstOrDefault(c => c.Room == target && !c.Dead && c.Suit == null);
                var lightLink = inside == null ? default : CrewWhy.Influences(inside, w).FirstOrDefault(l => l.Source == WhySource.Light);
                bool lit = !target.Dark;
                Check("영향 사슬 — 분전함 차단(침수) → 정전 → 캄캄함 → 작업 느림", lo.Count == 2 && lo[0].Contains("분전함") && lo[1] == $"{target.Name} 정전"
                    && (inside == null || lit || lightLink.Steps != null && lightLink.Steps[^2] == "캄캄함" && lightLink.Steps[^1].Contains("작업 20% 느림")),
                    $"{string.Join(" → ", lo)}{(inside != null && lightLink.Steps != null ? $" · {inside.Name}: {string.Join(" → ", lightLink.Steps)}" : lit ? " · 비상등이 있어 밝다" : " · 그 방에 사람 없음")}");

                // 공기 사슬: 산소 발생기가 모두 멎으면 그 고장이 사슬 맨 앞에
                foreach (var f in w.Ship.FurnitureOf(FurnitureType.OxygenGenerator)) if (f.Machine != null) w.Machines.Break(f.Machine, FaultKind.ElectrolyzerFault);
                var ao = CrewWhy.AirOrigin(w.Ship.Rooms.First(r => r.Kind == RoomType.Mess), w);
                Check("영향 사슬 — 방 산소가 모자라면 산소 발생기 고장이 맨 앞", ao.Count > 0 && ao[0].StartsWith("산소 발생기"), string.Join(" → ", ao));
            }

            // 1-d) 믿음 ≠ 세계: 데이터선이 끊긴 방의 불은 배도 사람도 모른다 → 아무도 끄러 가지 않는다 · 이어진 방이면 경보로 알고 끄러 간다
            {
                var wa = DayOne(seed, "Hanbit");
                var wb = DayOne(seed, "Hanbit");
                // 통신 중계 모듈이 없는 배 (중계가 있으면 끊긴 데이터선도 무선으로 이어져 이 장면이 생기지 않는다 — 두 배 모두 같게)
                foreach (var ww in new[] { wa, wb }) { ww.Automation.Remove(ComputerModule.CommsRelay); ww.Automation.V15NoAuto = true; } // 끊긴 선을 보고 스스로 중계를 올리지도 않는다
                // 사람도 로봇도 없는 한적한 방 (창고 · 화물칸부터)
                Room Pick(World w) => w.Ship.Rooms.Where(r => !r.Detached && r.Kind != RoomType.Corridor && w.Crew.All(c => c.Room != r) && w.Robots.Robots.All(b => b.Room != r))
                    .OrderBy(r => r.Kind is RoomType.Storage or RoomType.Cargo ? 0 : r.Kind is RoomType.Workshop or RoomType.Galley or RoomType.Mess or RoomType.Bridge ? 2 : 1).ThenBy(r => r.Id).First();
                var ra = Pick(wa);
                var rb = wb.Ship.Rooms[ra.Id];
                // 그 방으로 가는 데이터선을 끊는다 (감지기 경보가 닿지 않는다)
                foreach (var l in wa.Net.Links.Where(l => l.Kind == NetKind.Data && (l.Room == ra || l.Door != null && (l.Door.RoomA == ra || l.Door.RoomB == ra))).ToList())
                    wa.Net.Hurt(l, 1f, "시험");
                for (int t = 0; t < 600 && (t == 0 || ra.DataLinked); t++) { wa.Step(); wb.Step(); } // 망이 다시 계산될 때까지
                bool lit = false;
                foreach (var cell in ra.Cells.Skip(ra.Cells.Count / 2))
                    if (wa.Fire.Ignite(cell, 0.6f)) { wb.Fire.Ignite(cell, 0.6f); lit = true; break; }
                bool relay = ComputerV15.Relay(wa);
                int unawareA = -1, ordersA = 0, knowB = 0, goingB = 0, cutReason = 0, cutB = 0, unawareGoing = 0;
                var unawareIds = new HashSet<int>();
                bool knownA = false;
                string sampleA = "", sampleB = "", later = "";
                for (int t = 0; t < SimTime.Minutes(20) && lit; t++)
                {
                    wa.Step(); wb.Step();
                    if (unawareA < 0)
                    {
                        // ① 이어진 배가 경보로 알게 된 그 순간, 끊긴 배는 아무도 모른다 (작업도 없다)
                        var knowers = wb.Crew.Where(c => !c.Dead && c.CanAct).Select(c => (c, b: CrewWhy.Beliefs(c, wb, 8).FirstOrDefault(x => x.Kind == BeliefKind.Knows && x.Text.StartsWith($"{rb.Name} 불")))).Where(x => x.b.Text != null).ToList();
                        if (knowers.Count == 0) continue;
                        knowB = knowers.Count;
                        sampleB = $"{knowers[0].c.Name}: {knowers[0].b.Text}";
                        unawareA = 0;
                        foreach (var c in wa.Crew.Where(c => !c.Dead && c.CanAct))
                        {
                            var b = CrewWhy.Beliefs(c, wa, 8).FirstOrDefault(x => x.Kind == BeliefKind.Unaware && x.Text.StartsWith($"{ra.Name} 불"));
                            if (b.Text == null || !b.Wrong) continue;
                            unawareA++;
                            unawareIds.Add(c.Id);
                            if (b.Text.Contains("경보가 닿지 않는다") || b.Text.Contains("배도 아직 모른다")) cutReason++;
                            if (sampleA == "" || b.Text.Contains("경보가 닿지")) sampleA = $"{c.Name}: {b.Text}";
                        }
                        ordersA = wa.Board.Open.Count(o => o.Kind == WorkKind.Extinguish && o.Target.CurrentRoom == ra);
                        knownA = wa.Fire.IsKnown(ra);
                        cutB = wb.Crew.Count(c => CrewWhy.Beliefs(c, wb, 8).Any(x => x.Kind == BeliefKind.Unaware && x.Text.StartsWith($"{rb.Name} 불") && x.Text.Contains("경보가 닿지 않는다")));
                    }
                    // 모른다는 사람은 그 불을 끄러 가지 않는다 (모르는 사고의 일은 하지 않는다)
                    if (unawareA > 0) unawareGoing += wa.Crew.Count(c => unawareIds.Contains(c.Id) && c.Job?.Order is { Kind: WorkKind.Extinguish } o && o.Target.CurrentRoom == ra
                        && !c.Mind.Knows.ContainsKey($"fire:{ra.Id}"));
                    // ② 이어진 배는 누군가 끄러 간다
                    goingB = wb.Crew.Count(c => c.Job?.Order is { Kind: WorkKind.Extinguish } o && o.Target.CurrentRoom == rb);
                    if (goingB > 0) break;
                }
                // ③ 끊긴 배: 누군가 지나가다(냄새 · 연기) 보면 그때 안다 — 믿음이 바뀌고 일이 생긴다
                for (int t = 0; t < SimTime.Hours(2) && lit && !wa.Fire.IsKnown(ra) && wa.Fire.CountIn(ra) > 0; t++) wa.Step();
                if (wa.Fire.IsKnown(ra))
                {
                    var finder = wa.Crew.FirstOrDefault(c => CrewWhy.Beliefs(c, wa, 8).Any(x => x.Kind == BeliefKind.Knows && x.Text.StartsWith($"{ra.Name} 불")));
                    later = finder != null ? $" · 뒤에 {finder.Name}: {CrewWhy.Beliefs(finder, wa, 8).First(x => x.Kind == BeliefKind.Knows && x.Text.StartsWith($"{ra.Name} 불")).Text}" : " · 뒤에 배가 알게 됨";
                }
                else later = wa.Fire.CountIn(ra) == 0 ? " · 저절로 꺼짐" : " · 두 시간 동안 아무도 모름";
                // 주컴퓨터도 같은 규칙을 읽는다: 이어진 방은 화재 감지기 경보를 판단 기록에 남기고, 끊긴 방은 남기지 못한다
                bool alarmA = wa.Automation.Book.Acts.Any(x => x.Kind == ActKind.Alarm && x.RoomId == ra.Id);
                bool alarmB = wb.Automation.Book.Acts.Any(x => x.Kind == ActKind.Alarm && x.RoomId == rb.Id);
                later += $" · 컴퓨터 경보 기록 끊김 {alarmA} / 이어짐 {alarmB} · 끊긴 방 데이터선 {ra.DataLinked}";
                Check("믿음 — 데이터선이 끊긴 방의 불은 '모른다(경보가 닿지 않는다)'로 보이고 모르는 사람은 끄러 가지 않는다 · 이어진 방은 컴퓨터가 경보하고 사람이 알고 끄러 간다",
                    lit && !relay && unawareA > 0 && cutReason > 0 && unawareGoing == 0 && (knownA || ordersA == 0) && knowB > 0 && goingB > 0 && cutB == 0 && alarmB && !alarmA,
                    $"{ra.Name}: 끊김 — 모름 {unawareA}명(경보 끊김 탓 {cutReason}) · 모르는 채 끄러 감 {unawareGoing} · 배가 앎 {knownA} · 작업 {ordersA} ({sampleA}) | 이어짐 — 앎 {knowB}명 · 끄러 감 {goingB} ({sampleB}){later}{(relay ? " · 통신 중계가 있어 시험 무효" : "")}");

                // 목표 · 컴퓨터 신뢰 · 기술: 누구를 눌러도 목표가 있고, 컴퓨터가 있으면 신뢰 줄이 있다
                int noGoal = wb.Crew.Count(c => !c.Dead && CrewWhy.Goal(c).why.Length == 0 && c.Job != null);
                int trust = wb.Crew.Count(c => !c.Dead && CrewWhy.Beliefs(c, wb, 8).Any(x => x.Kind == BeliefKind.Trust));
                var skills = CrewWhy.TopSkills(wb.Crew[0], 3);
                Check("승무원 카드 — 목표(층 · 까닭) · 컴퓨터 신뢰 · 잘하는 기술 3", noGoal == 0 && (!wb.Automation.Present || trust == wb.Crew.Count(c => !c.Dead))
                    && skills.Count == 3 && skills[0].level >= skills[1].level && skills[1].level >= skills[2].level,
                    $"{wb.Crew[0].Name}: 목표 {CrewWhy.Goal(wb.Crew[0]).name} · {CrewWhy.Goal(wb.Crew[0]).why} · 기술 {string.Join(", ", skills.Select(s => $"{Skills.Name(s.skill)} {s.level:0.00}"))} · 신뢰 줄 {trust}명");
            }

            // 2) 자원 "언제 문제가 되나": 계산 자체
            {
                var s = new List<Sample>();
                for (int i = 0; i <= 12; i++) s.Add(new Sample(i / 6f, 21f - 0.1f * (i / 6f)));
                float? h = Readout.HoursUntil(s, 19.5f, falling: true, windowHours: 3f, minRate: 0.05f);
                var flat = new List<Sample>();
                for (int i = 0; i <= 12; i++) flat.Add(new Sample(i / 6f, 21f + (i % 2 == 0 ? 0.01f : -0.01f)));
                float? hf = Readout.HoursUntil(flat, 19.5f, falling: true, windowHours: 3f, minRate: 0.05f);
                var co2 = new List<Sample>();
                for (int i = 0; i <= 12; i++) co2.Add(new Sample(i / 6f, 0.3f + 0.2f * (i / 6f)));
                float? hc = Readout.HoursUntil(co2, 1.5f, falling: false, windowHours: 2f, minRate: 0.02f);
                Check("추정 — 시간당 0.1 kPa씩 줄면 20.8 kPa에서 19.5까지 13시간 · 평평하면 없음 · CO2 오름도", h is float a && MathF.Abs(a - 13f) < 0.05f && hf == null && hc is float c && MathF.Abs(c - 4f) < 0.05f
                    && Readout.When(13f) == "13시간 뒤" && Readout.When(0.5f) == "30분 뒤" && Readout.When(60f) == "2.5일 뒤",
                    $"산소 {h:0.00}시간 · 평평 {(hf == null ? "없음" : hf.ToString())} · CO2 {hc:0.00}시간 · {Readout.When(13f)}");
            }

            // 3) 실제 배: 평시엔 아무것도 떠오르지 않다가, 산소 발생기가 모두 멎으면 산소만 떠오른다
            {
                var w = DayOne(seed, "Hanbit");
                var watch = new ResourceWatch();
                for (int t = 0; t < SimTime.Hours(3); t++) { w.Step(); watch.Sample(w); }
                var calm = Enum.GetValues<ResourceKey>().Where(k => watch.Status(w, k).Surfaced).ToList();
                var gens = w.Ship.FurnitureOf(FurnitureType.OxygenGenerator).Select(f => f.Machine!).ToList();
                ResourceStatus o2 = default;
                float firstLeft = -1f;
                for (int t = 0; t < SimTime.Hours(4); t++)
                {
                    foreach (var m in gens) if (!m.Stopped) w.Machines.Break(m, FaultKind.ElectrolyzerFault); // 산소 발생기가 멎은 채로 (고쳐도 다시)
                    w.Step();
                    watch.Sample(w);
                    o2 = watch.Status(w, ResourceKey.Oxygen);
                    if (o2.Surfaced && firstLeft < 0f) { firstLeft = o2.HoursLeft ?? 0f; break; }
                }
                var water = watch.Status(w, ResourceKey.Water);
                var food = watch.Status(w, ResourceKey.Food);
                Check("자원 — 평시 조용 · 산소가 줄기 시작하면 산소만 '몇 시간 뒤 부족'으로 떠오른다",
                    calm.Count == 0 && o2.Surfaced && o2.HoursLeft is float && !water.Surfaced && !food.Surfaced,
                    $"평시 떠오름 [{string.Join(",", calm)}] · 발생기 {gens.Count}대 · 산소 {o2.Value:0.00} kPa · 기울기 {o2.SlopePerHour:0.000}/h · {(o2.HoursLeft is float l ? Readout.When(l) + " 부족" : "추정 없음")} · 탱크 {watch.Status(w, ResourceKey.AirTank).Value * 100:0}% · 물 {water.Surfaced} · 식량 {food.Surfaced}");
            }

            // 4) 기록 묶기 · 거르기 · 장소 찾기 (순수 함수)
            {
                var e = new List<LogEntry>
                {
                    new(1, LogKind.Ship, "창고 환기 댐퍼 자동 폐쇄", -1),
                    new(2, LogKind.Ship, "창고 환기 댐퍼 자동 폐쇄", -1),
                    new(3, LogKind.Ship, "창고 환기 댐퍼 자동 폐쇄", -1),
                    new(4, LogKind.Life, "식사하러 식당으로 간다", 2),
                    new(5, LogKind.Ship, "창고 환기 댐퍼 자동 폐쇄", -1),
                    new(6, LogKind.Warning, "주방 화재 진압", 1),
                    new(7, LogKind.Life, "식사하러 식당으로 간다", 3),
                };
                var g = Readout.Group(e, 10);
                bool groupsOk = g.Count == 4 && g[1].Count == 4 && g[1].First.Tick == 1 && g[1].Last.Tick == 5 && g[0].Count == 1 && g[3].Last.CrewId == 3;
                var onlyCrew = Readout.Group(e, 10, x => Readout.Matches(x, 2, null, null));
                var onlyWarn = Readout.Group(e, 10, x => Readout.Matches(x, -1, null, LogKind.Warning));
                var w = DayOne(seed, "Hanbit");
                var kitchen = w.Ship.Rooms.FirstOrDefault(r => r.Name.Length >= 2);
                var found = kitchen == null ? null : Readout.RoomIn($"{kitchen.Name} 화재 진압", w.Ship.Rooms);
                Check("기록 — 같은 줄은 ×n으로 묶이고(사이에 다른 줄이 끼어도) · 사람/종류로 걸러지고 · 글 속 방을 찾는다",
                    groupsOk && onlyCrew.Count == 1 && onlyWarn.Count == 1 && found == kitchen && Readout.RoomIn("아무 데도 아님", w.Ship.Rooms) == null,
                    $"묶음 {string.Join(" / ", g.Select(x => $"{x.Last.Text}×{x.Count}"))} · 사람 {onlyCrew.Count} · 경고 {onlyWarn.Count} · 방 {found?.Name}");

                // 실제 기록: 묶은 줄 수의 합 = 거른 줄 수, 이웃한 묶음이 같은 줄이 아니다
                var all = w.Log.Entries;
                var groups = Readout.Group(all, int.MaxValue, null, 3, all.Count);
                bool sum = groups.Sum(x => x.Count) == all.Count;
                bool apart = groups.Zip(groups.Skip(1)).All(z => z.First.Last.Text != z.Second.Last.Text || z.First.Last.CrewId != z.Second.Last.CrewId);
                Check("기록 — 실제 하루치 기록을 묶어도 줄을 잃지 않는다", sum && apart && groups.Count < all.Count,
                    $"기록 {all.Count}줄 → 묶음 {groups.Count} · 가장 많이 묶인 줄 {groups.OrderByDescending(x => x.Count).First().Last.Text}×{groups.Max(x => x.Count)}");
            }

            // 5) 결정론 — 같은 시드는 같은 지문 · 화면이 읽어 가도(사슬 · 자원 · 기록 묶기) 지문이 같다
            {
                uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
                uint a = H(), b = H();
                var wr = World.CreateDefault(seed, 0, "Hanbit");
                var watch = new ResourceWatch();
                for (long t = 0; t < SimTime.TicksPerDay + SimTime.Hours(6); t++)
                {
                    wr.Step();
                    if (t % 20 != 0) continue;
                    watch.Sample(wr);
                    foreach (var k in Enum.GetValues<ResourceKey>()) watch.Status(wr, k);
                    foreach (var c in wr.Crew) { CrewWhy.Chain(c, wr); CrewWhy.Influences(c, wr); CrewWhy.TopRelations(c, wr); CrewWhy.Things(c, wr); CrewWhy.RecentMemories(c); CrewWhy.Beliefs(c, wr); CrewWhy.Goal(c); CrewWhy.TopSkills(c); CrewWhy.InjuryOrigin(c, wr); }
                    foreach (var r in wr.Ship.Rooms) { CrewWhy.LightOrigin(r, wr); CrewWhy.AirOrigin(r, wr); }
                    Readout.Group(wr.Log.Entries, 8, x => Readout.Matches(x, -1, null, null));
                }
                uint c2 = SaveGame.StateHash(wr);
                Check("결정론 — 같은 시드 같은 지문 · 화면이 읽어 가도 같다", a == b && a == c2, $"{a:x8} / {b:x8} / 읽으며 {c2:x8}");
            }

            // 6) 아이콘: 자원 · 상태 · 방 70 · 설비 70 · 기술 7이 저마다 다른 그림 (같은 틀에 글자만 바꾼 것 · 같은 파일 금지)
            IconCheck();
            UiV24Checks(seed); // v16.24 확대 3단계 · 연대기 · 사고 카드 · 제안 때 · 화면 글
            UiV176Checks(seed); // v17.6 승무원 목록 · 색약 · 음악 · v17.9 도감
        }
        catch (Exception ex) { Console.WriteLine(ex); _fails++; }
        Console.WriteLine(_fails == 0 ? "\n✔ UI 기반 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }

    private static string UiTestsPath([System.Runtime.CompilerServices.CallerFilePath] string p = "") => p;

    /// <summary>
    /// 아이콘 점검 (화면 코드는 Godot이 있어야 돌므로, 짝짓기 표를 글로 읽는다):
    /// 방 · 설비 · 기술 종류마다 정확히 한 줄 · 서로 다른 아이콘 · 파일이 있다 · SVG에 글자(&lt;text&gt;)가 없다 · 같은 그림 파일이 둘 없다.
    /// </summary>
    private static void IconCheck()
    {
        string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(UiTestsPath())!, "..", ".."));
        string src = System.IO.Path.Combine(root, "game", "src", "View", "Icons.cs");
        string dir = System.IO.Path.Combine(root, "game", "assets", "icons");
        if (!System.IO.File.Exists(src) || !System.IO.Directory.Exists(dir)) { Check("아이콘 — 원본을 찾는다", false, src); return; }
        string code = System.IO.File.ReadAllText(src);

        // 한 함수의 switch 팔들: (종류 이름들, 아이콘)
        List<(string[] names, string icon)> Arms(string signature, string enumName)
        {
            int at = code.IndexOf(signature, StringComparison.Ordinal);
            int end = at < 0 ? -1 : code.IndexOf("};", at, StringComparison.Ordinal);
            var arms = new List<(string[], string)>();
            if (at < 0 || end < 0) return arms;
            var body = code[at..end];
            var re = new System.Text.RegularExpressions.Regex(@"((?:(?:ShipSim\.Core\.)?" + enumName + @"\.\w+\s*(?:or\s*)?)+)=>\s*""([\w-]+)""");
            foreach (System.Text.RegularExpressions.Match m in re.Matches(body))
            {
                var names = System.Text.RegularExpressions.Regex.Matches(m.Groups[1].Value, enumName + @"\.(\w+)").Select(x => x.Groups[1].Value).ToArray();
                arms.Add((names, m.Groups[2].Value));
            }
            return arms;
        }

        var files = System.IO.Directory.GetFiles(dir, "*.svg").ToDictionary(f => System.IO.Path.GetFileNameWithoutExtension(f), f => f);
        string Check1<T>(string label, string signature, string enumName) where T : struct, Enum
        {
            var arms = Arms(signature, enumName);
            var all = Enum.GetNames<T>();
            var mapped = arms.SelectMany(a => a.names).ToList();
            var missing = all.Where(n => !mapped.Contains(n)).ToList();
            var shared = arms.Where(a => a.names.Length > 1).Select(a => $"{string.Join("/", a.names)}→{a.icon}").ToList();
            var dupIcon = arms.GroupBy(a => a.icon).Where(g => g.Count() > 1).Select(g => $"{g.Key}×{g.Count()}").ToList();
            var noFile = arms.Where(a => !files.ContainsKey(a.icon)).Select(a => a.icon).Distinct().ToList();
            bool ok = all.Length > 0 && missing.Count == 0 && shared.Count == 0 && dupIcon.Count == 0 && noFile.Count == 0;
            Check($"아이콘 — {label} {all.Length}종이 저마다 다른 아이콘 (함께 쓰는 것 · 빠진 것 · 파일 없는 것 없음)", ok,
                $"짝 {arms.Count} · 빠짐 [{string.Join(",", missing.Take(6))}] · 함께 씀 [{string.Join(",", shared.Take(4))}] · 겹친 아이콘 [{string.Join(",", dupIcon.Take(4))}] · 파일 없음 [{string.Join(",", noFile.Take(4))}]");
            return string.Join(",", arms.Select(a => a.icon));
        }
        Check1<RoomType>("방 종류", "public static string Room(RoomType t)", "RoomType");
        Check1<FurnitureType>("설비 종류", "public static string Furniture(FurnitureType t)", "FurnitureType");
        Check1<Skill>("기술", "public static string Skill(", "Skill");

        // 자원 · 상태 · 개인 물건도 파일이 있다
        int mapAt = code.IndexOf("무엇에 어떤 아이콘", StringComparison.Ordinal);
        string maps = mapAt < 0 ? code : code[mapAt..];
        var used = System.Text.RegularExpressions.Regex.Matches(maps, @"(?:=>|\?|:|return)\s*""([a-z0-9-]+)""").Select(m => m.Groups[1].Value).Distinct().ToList();
        var lost = used.Where(n => !files.ContainsKey(n)).ToList();
        // SVG 자체: 글자 없음 · 단색(흰 선) · 같은 그림 파일 없음 · XML로 읽힌다
        var texty = new List<string>();
        var broken = new List<string>();
        var same = new Dictionary<string, string>();
        var dupFiles = new List<string>();
        foreach (var (name, path) in files.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            string svg = System.IO.File.ReadAllText(path);
            if (svg.Contains("<text", StringComparison.Ordinal)) texty.Add(name);
            try { System.Xml.Linq.XDocument.Parse(svg); } catch { broken.Add(name); }
            string body = System.Text.RegularExpressions.Regex.Replace(svg, @"\s+", "");
            if (same.TryGetValue(body, out var other)) dupFiles.Add($"{other}={name}");
            else same[body] = name;
        }
        Check("아이콘 — SVG 60개 이상 · 쓰는 이름은 모두 파일이 있다 · 글자 없음 · 같은 그림 없음 · 모두 읽힌다",
            files.Count >= 60 && lost.Count == 0 && texty.Count == 0 && dupFiles.Count == 0 && broken.Count == 0,
            $"SVG {files.Count}개 · 쓰는 이름 {used.Count} · 없음 [{string.Join(",", lost.Take(6))}] · 글자 [{string.Join(",", texty)}] · 같은 그림 [{string.Join(",", dupFiles)}] · 깨짐 [{string.Join(",", broken)}]");
    }
}
