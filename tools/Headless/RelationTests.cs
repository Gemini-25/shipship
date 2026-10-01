using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v14.4 관계의 이유 · 엇갈린 기억 · 목적 있는 대화
public static partial class Program
{
    private static int RunRelationTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"관계·대화 점검 (v14.4) · 시드 {seed}\n");
        try
        {
            // 1) 겪은 일이 관계의 이유로 남는다 (치료 · 가르침 · 물건 …)
            {
                var w = DayOne(seed, "Hanbit");
                foreach (var hurt in w.Crew.Where(c => !c.IsChild).Take(2)) NeedsSystem.AddInjury(hurt.Vitals, 0.4f, "넘어짐");
                Run(w, SimTime.TicksPerDay);
                var rel = w.Relations;
                var kinds = rel.All.GroupBy(m => m.Reason).Select(g => $"{RelationSystem.Name(g.Key)} {g.Count()}");
                var ex = rel.All.OrderByDescending(m => MathF.Abs(m.Weight)).Take(3).Select(m => $"{w.Crew[m.Who].Name}→{w.Crew[m.About].Name}: {m.Text}");
                Check("관계의 이유 — 겪은 일이 \"무엇 때문에 그런 사이인지\"로 남는다", rel.All.Count >= 3 && rel.All.Select(m => m.Reason).Distinct().Count() >= 2,
                    $"{rel.All.Count}개 · " + string.Join(" · ", kinds) + "\n      " + string.Join(" / ", ex) + $"\n      대화: {rel.Stats}");
            }
            // 2) 힘들어 보이는 동료에게 묻고, 다음 근무를 대신 선다
            {
                var w = DayOne(seed, "Hanbit");
                var tired = w.Crew.Where(c => !c.IsChild).Take(3).ToList();
                foreach (var c in w.Crew) foreach (var t in tired) if (c != t) c.ChangeAffinity(t, 0.3f);
                for (int h = 0; h < 36 && w.Relations.Stats.Covers == 0; h++)
                {
                    foreach (var t in tired) t.Needs.Stress = MathF.Max(t.Needs.Stress, 0.85f);
                    Run(w, SimTime.Hours(1));
                }
                var m = w.Relations.All.FirstOrDefault(x => x.Reason == RelationReason.DidMyShift);
                Check("걱정 — 힘들어 보이는 동료에게 이유를 묻고, 근무를 대신 선다", w.Relations.Stats.Worries >= 1 && w.Relations.Stats.Covers >= 1 && m != null,
                    $"{w.Relations.Stats} · " + (m != null ? $"{w.Crew[m.Who].Name}: \"{w.Crew[m.About].Name} — {m.Text}\" · 근무 쉼 {SimTime.Clock(w.Crew[m.Who].ExcusedUntil)}까지" : "-"));
            }
            // 3) 다친 사람을 위로하며 좋아하는 음식을 가져다준다
            {
                var w = DayOne(seed, "Hanbit");
                var hurt = w.Crew.First(c => !c.IsChild);
                NeedsSystem.AddInjury(hurt.Vitals, 0.45f, "넘어짐");
                foreach (var c in w.Crew) if (c != hurt) c.ChangeAffinity(hurt, 0.5f);
                Run(w, SimTime.Hours(24));
                var m = w.Relations.All.FirstOrDefault(x => x.Reason == RelationReason.Comforted && x.Who == hurt.Id);
                if (Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "2")
                    Console.WriteLine($"      {hurt.Name} 부상 {hurt.Vitals.Injury:0.00} 치료 {(hurt.Vitals.TreatedTick > 0 ? SimTime.Clock(hurt.Vitals.TreatedTick) : "-")} · 위로받은 이: " +
                        string.Join(", ", w.Relations.All.Where(x => x.Reason == RelationReason.Comforted).Select(x => $"{w.Crew[x.Who].Name}←{w.Crew[x.About].Name} {SimTime.Clock(x.Tick)}")));
                Check("위로 — 다친 사람에게 좋아하는 음식을 가져다준다", w.Relations.Stats.Comforts >= 1 && m != null,
                    $"위로 {w.Relations.Stats.Comforts} · {hurt.Name}(좋아하는 음식 {w.Relations.Favorite(hurt)}): {m?.Text}");
            }
            // 4) 신입에게 지난 사고와 바뀐 수칙을 일러 준다
            {
                var w = DayOne(seed, "Hanbit");
                w.History.Add(w, HistoryKind.Lesson, "교훈: 불을 여러 번 겪었다 — 식사 전에 소화기 자리를 확인한다", log: true);
                Run(w, SimTime.Hours(7));
                var rookie = w.Crew.First(c => !c.IsChild);
                w.Society.MarkJoined(rookie); rookie.Lessons.Clear(); // 새로 탔다
                foreach (var c in w.Crew) if (c != rookie) c.ChangeAffinity(rookie, 0.2f);
                if (Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "2")
                    for (int h = 0; h < 30; h++)
                    {
                        Run(w, SimTime.Hours(1));
                        if (h % 3 != 0) continue;
                        { var t0 = w.Crew.First(c => c != rookie && !c.IsChild); Console.WriteLine($"      설명 {t0.Name}→{rookie.Name}: {w.Relations.Explain(t0, rookie, w.Paths.Flood(t0.Cell, t0.PathProfile))} · 교훈 사건 {w.History.Events.Count(e => e.Kind == HistoryKind.Lesson)}"); }
                        Console.WriteLine($"      {SimTime.Clock(w.Tick)} 신입? {w.Relations.IsNewcomer(rookie)} · " + string.Join(" | ", w.Crew.Where(c => c != rookie && c.IsAwake).Take(6).Select(c =>
                        {
                            var p = w.Relations.PickTalk(c, w.Paths.Flood(c.Cell, c.PathProfile));
                            var e = c.LastEvaluations?.FirstOrDefault(ev => ev.Activity is ReachOutActivity);
                            return $"{c.Name}: {(p is var (t, tp, u, _) ? $"{t.Name}/{tp}/{u:0.00}" : "-")} 점수 {e?.Score:0.00} 지금 {c.Job?.Label}";
                        })));
                    }
                else Run(w, SimTime.Hours(30));
                var m = w.Relations.All.FirstOrDefault(x => x.Reason == RelationReason.TaughtMe && x.Who == rookie.Id && x.Text.StartsWith("지난 일"));
                Check("신입 — 지난 사고와 바뀐 수칙을 일러 준다 (들은 사람은 그 일을 안다)", w.Relations.Stats.Teachings >= 1 && rookie.Lessons.Count >= 1 && m != null,
                    $"가르침 {w.Relations.Stats.Teachings} · {rookie.Name}이 아는 지난 일 {rookie.Lessons.Count} · {m?.Text}");
            }
            // 5) 다툰 둘이 사과하고 화해한다
            {
                var w = DayOne(seed, "Hanbit");
                var a = w.Crew.First(c => !c.IsChild); var b = w.Crew.Last(c => !c.IsChild);
                a.ChangeAffinity(b, -0.6f - a.AffinityTo(b)); b.ChangeAffinity(a, -0.5f - b.AffinityTo(a));
                a.Quarrel = b.Quarrel = w.Tick;
                a.Habits.Clear(); a.Habits.Add(Habit.Patient); b.Habits.Clear(); b.Habits.Add(Habit.Patient);
                float before = b.AffinityTo(a);
                Run(w, SimTime.Hours(30));
                Check("화해 — 다툰 둘 중 한 사람이 먼저 사과하고, 받아 주면 사이가 풀린다", w.Relations.Stats.Apologies >= 1,
                    $"사과 {w.Relations.Stats.Apologies} · 화해 {w.Relations.Stats.Reconciled} · 거절 {w.Relations.Stats.Refused} · {b.Name}→{a.Name} {before:0.00} → {b.AffinityTo(a):0.00}");
            }
            // 6) 엇갈린 기억: 쓰러진 사람 곁을 떠난 사람 → "나를 두고 갔다" → 진실을 들으면 바뀐다
            {
                var w = DayOne(seed, "Hanbit");
                var v = w.Crew.First(c => !c.IsChild && c.Room != null && c.IsAwake);
                // 곁에 둘이 서 있다 (한 사람이 구하러 돌아오면 다른 한 사람은 "두고 간" 사람이 된다)
                var xs = w.Crew.Where(c => c != v && !c.IsChild && c.IsAwake && c.CanAct).Take(2).ToList();
                foreach (var (x0, k) in xs.Select((x0, k) => (x0, k)))
                {
                    x0.EndJob(w, ToilStatus.Interrupted);
                    x0.Position = v.Position + new System.Numerics.Vector2(1.2f, 0.6f * k);
                    x0.PreviousPosition = x0.Position;
                    x0.HoldUntil = w.Tick + SimTime.Minutes(2); x0.HoldWhy = "시험: 곁에 서 있다"; x0.NextThinkTick = w.Tick;
                }
                Run(w, 2);
                v.Vitals.Health = 0.1f;
                Run(w, SimTime.Minutes(1));
                bool down = v.Down;
                // 둘은 우주복을 가지러 간 척 (다른 방으로) — 구하러 오는 건 그다음 판단
                var away = w.Ship.Rooms.First(r => r != v.Room && !r.Detached && r.Cells.Any(cl => w.Ship.IsOpenFloor(cl)));
                foreach (var x0 in xs)
                {
                    x0.Position = away.Cells.First(cl => w.Ship.IsOpenFloor(cl)).Center; x0.PreviousPosition = x0.Position;
                    x0.EndJob(w, ToilStatus.Interrupted); x0.NextThinkTick = w.Tick;
                }
                Run(w, SimTime.Minutes(1));
                v.Vitals.Health = 0.6f; v.Vitals.Oxygen = 1f;
                for (int mi = 0; mi < 180 && (mi < 2 || v.Down); mi++) Run(w, SimTime.Minutes(1)); // 깨어날 때까지
                if (Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "2")
                    Console.WriteLine($"      {SimTime.Clock(w.Tick)} {v.Name} 쓰러짐 {v.Down} 업힘 {v.CarriedBy?.Name} · 두고 감 {w.Relations.Stats.Abandons} · {v.Name}의 기억 " + string.Join(", ", w.Relations.All.Where(mm => mm.Who == v.Id).Select(mm => $"{w.Crew[mm.About].Name}:{RelationSystem.Name(mm.Reason)}")));
                var m = w.Relations.All.FirstOrDefault(mm => mm.Who == v.Id && xs.Any(x0 => x0.Id == mm.About) && mm.Reason == RelationReason.AbandonedMe);
                string first = m?.Text ?? "-";
                float w0 = m?.Weight ?? 0f;
                for (int h = 0; h < 36 && m is { Revealed: false }; h++) Run(w, SimTime.Hours(1));
                Check("엇갈린 기억 — \"나를 두고 갔다\"고 기억하다가, 진실을 들으면 바뀐다", down && m != null && m.Truth != null && m.Revealed && m.Weight > w0,
                    $"{v.Name} 쓰러짐 {(down ? "예" : "아니오")} · 처음: \"{first}\" ({w0:0.00}) · 진실: {m?.Truth} · 지금: \"{m?.Text}\" ({m?.Weight:0.00}) · 진실을 말함 {w.Relations.Stats.Truths}");
            }
            // 7) 고장 소문: 당직 기록을 들은 사람이 확인하러 간다 (믿으면 판단까지 받아들이고, 못 믿으면 직접 본다)
            {
                var w = DayOne(seed, "Hanbit");
                var m = w.Ship.Machines.First(mm => mm.Body.Type == FurnitureType.CoolantPump);
                var o = new Omen { Kind = OmenKind.Vibration, Fault = FaultKind.BearingWear, Cause = OmenCause.BearingWear, Since = w.Tick, Due = w.Tick + SimTime.Hours(40) };
                m.Omen = o;
                var author = w.Crew.Where(c => !c.IsChild).OrderBy(c => c.RawSkill(Skill.Mechanics)).First(); // 고칠 줄 모르는 사람이 봤다
                w.Watch.NoSensors = true; // 컴퓨터 일지로는 퍼지지 않게 — 사람의 입으로만
                w.Watch.Observe(m, o, "당직", author, null);
                w.Policies.Set("violations", 1, "시험"); w.Society.Punish(author, "시험", light: false); // 본 사람은 근무 박탈 — 직접 고치지 못한다 (말은 한다)
                var note = o.Note!;
                foreach (var c in w.Crew) c.ChangeAffinity(author, 0.3f);
                int h0 = note.Holders.Count;
                Run(w, SimTime.Hours(30));
                var st = w.Relations.Stats;
                Check("고장 소문 — 잡담으로 전해지고, 들은 사람이 직접 확인하러 간다", st.Rumors >= 1 && note.Holders.Count > h0,
                    $"소문 {st.Rumors} · 확인 {st.Checks} · 못 믿음 {st.Distrusted} · 아는 사람 {h0} → {note.Holders.Count} · 길: {string.Join(" / ", note.Trail.TakeLast(Environment.GetEnvironmentVariable("SHIPSIM_DEBUG") == "2" ? 12 : 3))}");
            }
            // 8) 기억이 판단에 걸린다: 구해 준 사람의 말은 무겁고, 경고를 무시한 사람은 덜 믿는다 · 안 돌려준 사람에겐 공구를 빌려주지 않는다
            {
                var w = DayOne(seed, "Hanbit");
                var a = w.Crew[0]; var b = w.Crew[1]; var d = w.Crew[2];
                w.Relations.Remember(a, b, RelationReason.SavedMe, "시험: 구해 줬다");
                w.Relations.Remember(a, d, RelationReason.IgnoredMyWarning, "시험: 경고를 무시했다");
                float tb = w.Relations.Trust(a, b), td = w.Relations.Trust(a, d);
                Check("판단 — 구해 준 사람은 믿고, 경고를 무시한 사람은 덜 믿는다", tb > 0.3f && td < -0.1f, $"{a.Name}→{b.Name} {tb:0.00} · {a.Name}→{d.Name} {td:0.00} · 이유: {w.Relations.Why(a, b)?.Text} / {w.Relations.Why(a, d)?.Text}");
            }
            // 9) 결정론
            {
                uint H()
                {
                    var w = World.CreateDefault(seed, 0, "Hanbit");
                    Run(w, SimTime.TicksPerDay + SimTime.Hours(6));
                    return SaveGame.StateHash(w);
                }
                uint x = H(), y = H();
                Check("결정론 — 관계와 대화가 든 배도 같은 시드 같은 지문", x == y, $"{x:x8} / {y:x8}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"예외: {e}");
            _fails++;
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 관계·대화 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
