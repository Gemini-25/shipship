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
                    pa = wa.Crew.FirstOrDefault(c => c.Job?.Current is WorkToil && c.Vitals.Injury < 0.01f && c.Helper == null);
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
                    foreach (var c in wr.Crew) { CrewWhy.Chain(c, wr); CrewWhy.Influences(c, wr); CrewWhy.TopRelations(c, wr); CrewWhy.Things(c, wr); CrewWhy.RecentMemories(c); }
                    Readout.Group(wr.Log.Entries, 8, x => Readout.Matches(x, -1, null, null));
                }
                uint c2 = SaveGame.StateHash(wr);
                Check("결정론 — 같은 시드 같은 지문 · 화면이 읽어 가도 같다", a == b && a == c2, $"{a:x8} / {b:x8} / 읽으며 {c2:x8}");
            }
        }
        catch (Exception ex) { Console.WriteLine(ex); _fails++; }
        Console.WriteLine(_fails == 0 ? "\n✔ UI 기반 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
