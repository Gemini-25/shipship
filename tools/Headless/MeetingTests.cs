using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v13.2 회의 개편: 첫 출항 회의 · 정기 회의(실제로 모인다) · 사후 검토 · 토론과 설득 · 현장 협의 · 긴급 판단 · 결정의 무게 · 파벌 · 방침 1차
public static partial class Program
{
    private static string PolicyLine(World w) => string.Join(" · ", PolicySystem.All.Where(p => w.Policies[p.Id] != p.Default).Select(p => $"{p.Name} {w.Policies.Option(p.Id)}"));

    private static int RunMeetingTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"회의·방침 점검 (v13.2) · 시드 {seed}\n");
        bool debug = Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "1";
        try
        {
            // 1) 첫 출항 회의: 가치관대로 방침을 정한다 → 배마다 다르다
            {
                var cultures = new List<string>();
                var sets = new HashSet<string>();
                foreach (var (ship, s) in new[] { ("Mirinae", seed), ("Hanbit", seed), ("Eunha", seed), ("Mirinae", seed + 1), ("Hanbit", seed + 2), ("Kestrel", seed + 3) })
                {
                    var w = World.CreateDefault(s, 0, ship);
                    Run(w, SimTime.Minutes(5));
                    var maiden = w.Meetings.Minutes.FirstOrDefault(m => m.Kind == MeetingKind.Maiden);
                    string line = PolicyLine(w);
                    sets.Add(line);
                    cultures.Add($"{ship}/{s}: {w.Meetings.Culture} [{(line == "" ? "처음 값 그대로" : line)}]");
                }
                Check("첫 출항 회의 — 승무원 가치관대로 방침을 정해 배마다 문화가 다르다", sets.Count >= 2, string.Join("\n      ", cultures));
            }
            // 2) 정기 회의: 저녁 7시, 회의실(식당)에 실제로 모여 안건을 정한다
            {
                var w = World.CreateDefault(seed, 0, "Mirinae");
                int maxPresent = 0;
                string venue = "";
                MeetingRecord? rec = null;
                for (int m = 0; m < 60 * 30 && rec == null; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    var mt = w.Meetings;
                    if (mt.Venue is Room v)
                    {
                        venue = v.Name;
                        maxPresent = Math.Max(maxPresent, w.Crew.Count(c => !c.Dead && c.Room == v && c.Job?.Activity is MeetingActivity));
                    }
                    rec = mt.Minutes.FirstOrDefault(r => r.Kind is MeetingKind.Regular or MeetingKind.Review);
                }
                Check("정기 회의 — 하루 한 번 (일과가 가장 많이 겹치는 때) 회의실(식당)로 모여 앉아 회의한다", rec != null && maxPresent >= 2,
                    rec != null ? $"{SimTime.Clock(rec.Tick)} {rec.Venue} · 모인 사람 최대 {maxPresent} · 참석 {rec.Attendees.Count} · 안건 {rec.Items.Count}" + (rec.Items.Count > 0 ? $" ({string.Join(" / ", rec.Items.Select(i => $"{i.Title} {i.Outcome}"))})" : "")
                        : $"회의 없음 · 모인 곳 {venue} · 최대 {maxPresent}");
            }
            // 3) 사후 검토: 사고가 남긴 방침 안건을 다음 정기 회의에서 토론하고 표결한다 (컴퓨터 권고 포함)
            {
                var w = DayOne(seed, "Mirinae");
                w.Meetings.QueueReview("vacuumfire", 0, "창고 소화 수순 중에 사람이 쓰러졌다");
                AgendaItem? item = null;
                for (int h = 0; h < 30 && item == null; h++)
                {
                    Run(w, SimTime.Hours(1));
                    item = w.Meetings.Minutes.Where(r => r.Kind == MeetingKind.Review).SelectMany(r => r.Items).FirstOrDefault(i => i.Topic == "policy:vacuumfire");
                }
                var line = w.History.Events.LastOrDefault(e => e.Text.Contains("사후 검토"));
                Check("사후 검토 — 다음 정기 회의에 그 방침을 올려 토론·표결한다", item != null && item.Speeches.Count >= 2 && line != null,
                    item != null ? $"{item.Title} · 찬성 {item.Yes} · 반대 {item.No} → {item.Outcome} · 발언 {item.Speeches.Count} · {item.Computer ?? "(컴퓨터 권고 없음)"}\n      {line?.Text}" : "안건 없음");
                if (item != null)
                    foreach (var sp in item.Speeches) Console.WriteLine($"      - {w.Crew.First(c => c.Id == sp.Who).Name} ({(sp.For ? "찬" : "반")}): {sp.Text}" + (sp.Moved > 0 ? $" → {sp.Moved}명 움직임" : ""));
            }
            // 4) 토론과 설득: 설득력 있는 한 사람이 다수를 뒤집는다
            {
                var w = DayOne(seed, "Mirinae");
                var voters = w.Crew.Where(c => c.CanAct).Take(5).ToList();
                var lead = voters.OrderByDescending(c => c.Traits.Sociability + c.Traits.Calm).First();
                foreach (var c in voters.Where(c => c != lead)) { c.ChangeAffinity(lead, 0.6f); c.Needs.Stress = 0f; }
                lead.Needs.Stress = 0f;
                var item = new AgendaItem { Title = "시험 안건", Topic = "test" };
                var (yes, no) = w.Meetings.Debate(voters, c => c == lead ? (1f, "내가 해 봤다 — 된다") : (-0.04f, "글쎄"), c => c == lead ? 1f : 0.1f, item, lead);
                Check("토론 — 처음엔 반대가 많아도 설득력 있는 사람의 발언이 뒤집는다 (○○의 설득으로 뒤집혔다)", item.FlippedBy == lead.Name && yes.Count > no.Count,
                    $"처음 찬성 1 · 반대 {voters.Count - 1} → 찬성 {yes.Count} · 반대 {no.Count} · 뒤집은 사람 {item.FlippedBy ?? "-"}");
            }
            // 5) 긴급 판단: 급한 결정은 선장이 혼자 (선장을 바꾸면 바뀐 선장이)
            {
                var w = DayOne(seed, "Mirinae");
                w.Policies.Set("election", 2, "시험");
                var old = w.Command.Captain;
                w.Command.Elect(w.Crew.Where(c => c.CanAct).ToList(), old);
                var cap = w.Command.Captain;
                var judge = Council.Judge(w, true);
                Check("긴급 판단 — 급한 결정은 선장이 혼자 정한다", cap != null && cap != old && judge == cap, $"선장 {old?.Name} → {cap?.Name} · 긴급 판단 {judge?.Name}");
            }
            // 6) 현장 협의: 위기 중 결정은 지휘자·조장들이 무전으로 1~2분 안에
            {
                var w = DayOne(seed, "Mirinae");
                w.Policies.Set("inertfire", 0, "시험");
                w.Policies.Set("vacuumfire", 0, "시험");
                // 먹을 것을 거의 다 버려 배급 결정이 올라오게 하고, 불로 위기를 만든다
                foreach (var f in w.Ship.Furniture.Where(f => f.Storage != null))
                    foreach (var k in new[] { ItemKind.Meal, ItemKind.Ration, ItemKind.Produce }) f.Storage!.Take(k, 999);
                foreach (var bed in w.Ship.FurnitureOf(FurnitureType.GrowBed)) bed.Machine!.Crop = null; // 재배대도 비었다
                // 하루 남짓 먹을 것 (급하지는 않다 — 혼자 정하지 않고 협의한다)
                int meals = (int)(w.Crew.Count(c => !c.Dead) * FoodPolicy.MealsPerPersonDay * 1.3f);
                w.Ship.FurnitureOf(FurnitureType.Fridge).First().Storage!.Add(ItemKind.Meal, meals);
                BigFire(w, StoreRoom(w), 6);
                MeetingRecord? field = null;
                for (int m = 0; m < 90 && field == null; m++)
                {
                    Run(w, SimTime.Minutes(1));
                    field = w.Meetings.Minutes.FirstOrDefault(r => r.Kind == MeetingKind.Field);
                }
                var line = w.History.Events.LastOrDefault(e => e.Text.Contains("현장 협의"));
                if (debug) foreach (var e in w.History.Events.Where(e => e.Text.Contains("배급") || e.Text.Contains("회의"))) Console.WriteLine($"      {SimTime.Clock(e.Tick)} {e.Text}");
                Check("현장 협의 — 위기 중 결정은 지휘자·조장들이 무전으로 짧게", field != null && line != null,
                    field != null ? $"{SimTime.Clock(field.Tick)} {field.Venue} · {field.Attendees.Count}명 · {line?.Text}" : $"현장 협의 없음 · 지휘 {(w.Command.Active ? "중" : "없음")}");
            }
            // 7) 결정의 무게: 정한 일 뒤에 사람이 죽으면 찬성한 사람은 죄책감, 반대한 사람은 비난 (신용·관계가 떨어진다)
            {
                var w = DayOne(seed, "Mirinae");
                var live = w.Crew.Where(c => c.CanAct).ToList();
                var (a, b, victim) = (live[0], live[1], live[2]);
                var room = victim.Room!;
                float aff0 = b.AffinityTo(a), cred0 = w.Meetings.Credibility(a);
                w.Meetings.Record($"{room.Name} 재개방", "order:ReopenRoom", room.Id, a, new List<CrewMember> { a }, new List<CrewMember> { b }, "");
                w.CrewCanDie = true;
                victim.Vitals.Health = 0f;
                victim.Down = true;
                Run(w, SimTime.Minutes(2));
                Check("결정의 무게 — 정한 사람에게 죄책감, 반대한 사람의 비난 (관계·신용 하락)",
                    victim.Dead && w.Meetings.Guilt(a) > 0.2f && b.AffinityTo(a) < aff0 && w.Meetings.Credibility(a) < cred0 && w.Meetings.Blames >= 1,
                    $"{victim.Name} {(victim.Dead ? "죽음" : "살아 있음")} · {a.Name} 죄책감 {w.Meetings.Guilt(a):0.00} · 신용 {cred0:0.00}→{w.Meetings.Credibility(a):0.00} · {b.Name}→{a.Name} 관계 {aff0:0.00}→{b.AffinityTo(a):0.00}");
            }
            // 8) 파벌: 표가 가치관대로 갈리면 그 둘 사이에 골이 생긴다
            {
                var w = DayOne(seed, "Mirinae");
                var live = w.Crew.Where(c => c.CanAct).Take(4).ToList();
                live[0].Value = live[1].Value = CrewValue.Safety;
                live[2].Value = live[3].Value = CrewValue.Efficiency;
                w.Meetings.Split(live.Take(2).ToList(), live.Skip(2).ToList());
                w.Meetings.Split(live.Take(2).ToList(), live.Skip(2).ToList());
                float t = w.Meetings.Tension(CrewValue.Safety, CrewValue.Efficiency);
                Check("파벌 — 가치관대로 갈린 표가 쌓이면 골이 깊어진다", t > 0.1f && w.Meetings.Factions().Any(), $"안전↔효율 골 {t:0.00} · 파벌 {string.Join(", ", w.Meetings.Factions().Select(f => $"{MeetingSystem.ValueName(f.value)} {f.members.Count}명"))}");
            }
            // 9) 방침 1차 — 대피 기준 · 물 · 비축 · 배급 · 자동 실행 범위
            {
                var w = DayOne(seed, "Mirinae");
                var c = w.Crew.First(x => x.CanAct);
                var room = c.Room!;
                room.Air.O2 = 16.2f;
                float[] danger = new float[3];
                for (int k = 0; k < 3; k++) { w.Policies.Set("evac", k, "시험"); danger[k] = EvacuateActivity.DangerHere(c, w); }
                room.Air.O2 = 21f;
                w.Policies.Set("evac", 1, "시험");
                float use0 = 0, use2 = 0;
                w.Policies.Set("water", 0, "시험"); w.Water.Update(w, 0.01f); use0 = w.Water.Consumed;
                w.Policies.Set("water", 2, "시험"); w.Water.Update(w, 0.01f); use2 = w.Water.Consumed;
                w.Policies.Set("water", 0, "시험");
                w.Policies.Set("stock", 2, "시험"); float stock = w.History.Doctrine.StockScale; w.Policies.Set("stock", 1, "시험");
                // 자동 실행 범위: 경보만이면 감압된 방의 격벽이 저절로 닫히지 않는다
                bool[] locked = new bool[2];
                for (int k = 0; k < 2; k++)
                {
                    var w2 = DayOne(seed, "Mirinae");
                    w2.Policies.Set("autoscope", k == 0 ? 0 : 2, "시험");
                    w2.Policies.Set("decompress", 1, "시험");
                    var lounge = w2.Ship.RoomsOf(RoomType.Lounge).First();
                    ClearRoom(w2, lounge);
                    var wall = w2.Ship.Walls.Where(kv => kv.Value.IsHull && Hull.InsideRoom(w2.Ship, kv.Key) == lounge).Select(kv => kv.Key).First();
                    Hull.Damage(w2.Ship, wall, 1.2f);
                    Run(w2, SimTime.Minutes(1f)); // 사람이 손으로 잠그러 오기 전
                    // 통합: v16.19 차압 문은 기계라 방침과 상관없이 쾅 닫힌다 — 방침이 바꾸는 것은 주 컴퓨터가 격벽을 잠갔나 (조치 기록)
                    locked[k] = w2.Automation.Book.Acts.Any(a => a.Key == "lock:" + lounge.Id) || lounge.Doors.Where(d => !d.IsExternal).Any(d => d.Locked && !w2.Failsafe.Latched(d));
                    if (debug) Console.WriteLine($"      [{k}] 새는가 {lounge.Leaking} · 잠금 {lounge.Lockdown} · 대기 {lounge.LockPendingUntil} · 문 {string.Join(",", lounge.Doors.Select(d => $"{(d.IsExternal ? "외" : "")}{(d.Locked ? "L" : "-")}{(d.Powered ? "P" : "x")}"))} · 압력 {lounge.Air.Pressure:0}");
                }
                Check("방침 1차 — 대피 기준·물·비축·자동 실행 범위가 실제로 행동을 바꾼다",
                    danger[0] > danger[1] && danger[1] > danger[2] && use2 < use0 && stock > 1f && !locked[0] && locked[1],
                    $"산소 16.2에서 위험 일찍 {danger[0]:0.00} · 보통 {danger[1]:0.00} · 버티며 {danger[2]:0.00} · 물 {use0:0.00}→{use2:0.00}L/h · 비축 ×{stock:0.00} · 경보만 격벽 {(locked[0] ? "닫힘" : "열림")} · 전부 {(locked[1] ? "닫힘" : "열림")}");
            }
            // 10) 방침 화면 재료: 26개 방침 · 바뀐 이력 · 회의록
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                Run(w, SimTime.TicksPerDay * 2);
                var areas = PolicySystem.All.GroupBy(p => p.Area).Select(g => $"{g.Key} {g.Count()}");
                Check("방침 1차 — 재난·지휘·자원 26개, 회의록과 바뀐 이력이 남는다", PolicySystem.All.Length >= 26 && w.Meetings.Minutes.Count >= 2,
                    $"{string.Join(" · ", areas)} · 회의록 {w.Meetings.Minutes.Count}장 ({string.Join(", ", w.Meetings.Minutes.GroupBy(m => m.Kind).Select(g => $"{MeetingSystem.KindName(g.Key)} {g.Count()}"))}) · 바뀐 방침 {w.Policies.Changes.Count} · 정기 회의 {w.Meetings.Held} · 걸른 회의 {w.Meetings.Postponed}");
                if (debug)
                    foreach (var r in w.Meetings.Minutes)
                        Console.WriteLine($"      [{MeetingSystem.KindName(r.Kind)}] {SimTime.Clock(r.Tick)} {r.Venue} {r.Attendees.Count}명: " + string.Join(" / ", r.Items.Select(i => $"{i.Title} {i.Yes}:{i.No} {i.Outcome}{(i.FlippedBy != null ? " (뒤집음 " + i.FlippedBy + ")" : "")}")));
            }
            // 11) 결정론
            {
                uint H()
                {
                    var w = World.CreateDefault(seed, 0, "Mirinae");
                    Run(w, SimTime.TicksPerDay + SimTime.Hours(4));
                    return SaveGame.StateHash(w);
                }
                uint x = H(), y = H();
                Check("결정론 — 회의가 든 배도 같은 시드 같은 지문", x == y, $"{x:x8} / {y:x8}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"예외: {e}");
            _fails++;
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 회의·방침 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
