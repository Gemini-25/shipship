using System;
using System.Collections.Generic;
using System.Linq;
using ShipSim.Core;

// v14.0 승무원 개성: 경력 40 · 습관 40 · 자격 20 · 취미 30 · 두려움 20 · 말버릇 30
public static partial class Program
{
    private static int RunPersonaTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"승무원 개성 점검 (v14.0) · 시드 {seed}\n");
        try
        {
            // 1) 목록: 경력 40 · 습관 40 · 자격 20 · 취미 30 · 두려움 20 · 말버릇 30 — 이름이 겹치지 않는다
            {
                bool sizes = Persona.Backgrounds.Length == 40 && Persona.Habits.Length == 40 && Persona.Quals.Length == 20
                             && Persona.Hobbies.Length == 30 && Persona.Fears.Length == 20 && Persona.Quirks.Length == 30;
                bool enums = Enum.GetValues<Background>().Length == 40 && Enum.GetValues<Habit>().Length == 40 && Enum.GetValues<Qual>().Length == 20
                             && Enum.GetValues<Hobby>().Length == 30 && Enum.GetValues<Fear>().Length == 20;
                bool unique = Persona.Backgrounds.Select(b => b.Name).Distinct().Count() == 40 && Persona.Habits.Select(h => h.Name).Distinct().Count() == 40
                              && Persona.Quals.Select(q => q.Name).Distinct().Count() == 20 && Persona.Hobbies.Select(h => h.Name).Distinct().Count() == 30
                              && Persona.Fears.Select(f => f.Name).Distinct().Count() == 20;
                bool covered = Enum.GetValues<Background>().All(b => Persona.Backgrounds.Any(x => x.Id == b)) && Enum.GetValues<Habit>().All(h => Persona.Habits.Any(x => x.Id == h))
                               && Enum.GetValues<Qual>().All(q => Persona.Quals.Any(x => x.Id == q));
                bool quirks = Persona.Quirks.All(q => q.Contains("{0}"));
                Check("목록 — 경력 40 · 습관 40 · 자격 20 · 취미 30 · 두려움 20 · 말버릇 30", sizes && enums && unique && covered && quirks,
                    $"경력 {Persona.Backgrounds.Length} · 습관 {Persona.Habits.Length} · 자격 {Persona.Quals.Length} · 취미 {Persona.Hobbies.Length} · 두려움 {Persona.Fears.Length} · 말버릇 {Persona.Quirks.Length}");
            }
            // 2) 여러 배에 걸쳐 고루 나온다 — 사람마다 취미·말버릇, 대부분 두려움 하나
            {
                var people = new List<CrewMember>();
                string sample = "";
                foreach (var (ship, s) in new[] { ("Hanbit", seed), ("Mirinae", seed + 1), ("Kestrel", seed + 2), ("Hanbit", seed + 3), ("Mirinae", seed + 4), ("Hanbit", seed + 5), ("Mirinae", seed + 6), ("Kestrel", seed + 7) })
                {
                    var w = World.CreateDefault(s, 0, ship);
                    people.AddRange(w.Crew);
                    if (sample.Length == 0) sample = string.Join("\n      ", w.Crew.Take(3).Select(c => $"{c.Name}: {Life.Profile(c)} · “{Persona.Say(c, "알겠어")}”"));
                }
                int bgs = people.Select(c => c.Background).Distinct().Count();
                int habits = people.SelectMany(c => c.Habits).Distinct().Count();
                int hobbies = people.SelectMany(c => c.Hobbies).Distinct().Count();
                int fears = people.SelectMany(c => c.Fears).Distinct().Count();
                int quirks = people.Select(c => c.Quirk).Distinct().Count();
                bool all = people.All(c => c.Hobbies.Count >= 1 && c.Quirk >= 0);
                float feared = people.Count(c => c.Fears.Count > 0) / (float)people.Count;
                Check("고루 나온다 — 경력·습관·취미·두려움·말버릇이 사람마다", all && bgs >= 22 && habits >= 28 && hobbies >= 22 && fears >= 15 && quirks >= 20 && feared is > 0.5f and < 0.9f,
                    $"{people.Count}명 · 경력 {bgs}가지 · 습관 {habits} · 취미 {hobbies} · 두려움 {fears} (있는 사람 {feared * 100:0}%) · 말버릇 {quirks}\n      {sample}");
            }
            // 3) 역할에 어울리는 경력 — 의무관은 의대·간호·구급, 기술자는 광부·용접·배관…
            {
                int fit = 0, n = 0;
                foreach (var s in Enumerable.Range(seed, 10))
                {
                    var w = World.CreateDefault(s, 0, "Hanbit");
                    foreach (var c in w.Crew) { var f = Persona.Fits(c.Role); if (f.Length == 0) continue; n++; if (f.Contains(c.Background)) fit++; }
                }
                Check("역할에 어울리는 경력이 잦다", n > 0 && fit / (float)n > 0.45f, $"어울리는 경력 {fit}/{n} ({100f * fit / Math.Max(1, n):0}%)");
            }
            // 4) 습관 — 실수 확률 (완벽주의 < 보통 < 서두름) · 설득력 (앞장서기 > 따르기)
            {
                var w = DayOne(seed, "Hanbit");
                var c = w.Crew.First(x => x.CanAct);
                var o = w.Board.Open.FirstOrDefault() ?? new WorkOrder { Kind = WorkKind.Maintain, Skill = Skill.Mechanics };
                var keep = c.Habits.ToList();
                float Odds(params Habit[] hs) { c.Habits.Clear(); c.Habits.AddRange(hs); return w.Life.MistakeOdds(c, o).p; }
                float Persuade(params Habit[] hs) { c.Habits.Clear(); c.Habits.AddRange(hs); return w.Meetings.Persuasion(c, 0.5f); }
                float perf = Odds(Habit.Perfectionist), plain = Odds(), hasty = Odds(Habit.Hasty), forget = Odds(Habit.Forgetful, Habit.Messy);
                c.Habits.Clear(); c.Habits.Add(Habit.Hasty);
                string why = w.Life.MistakeOdds(c, o).why;
                float lead = Persuade(Habit.Leader), follow = Persuade(Habit.Follower);
                c.Habits.Clear(); c.Habits.AddRange(keep);
                Check("습관 — 실수 확률(완벽주의<보통<서두름<깜빡+덜렁) · 설득력(앞장서기>따르기)", perf < plain && plain < hasty && hasty < forget && lead > follow,
                    $"완벽주의 {perf * 100:0.0}% · 보통 {plain * 100:0.0}% · 서두름 {hasty * 100:0.0}% ({why}) · 깜빡+덜렁 {forget * 100:0.0}% · 설득력 앞장서기 {lead:0.00} / 따르기 {follow:0.00}");
            }
            // 5) 습관 — 하루를 보내면 걱정 많고 비관적인 사람이 낙천가보다 더 지친다
            {
                float Stress(params Habit[] hs)
                {
                    var w = DayOne(seed, "Mirinae");
                    var c = w.Crew.First(x => x.CanAct);
                    c.Habits.Clear(); c.Habits.AddRange(hs);
                    float sum = 0f;
                    for (int h = 0; h < 24; h++) { Run(w, SimTime.Hours(1)); sum += c.Needs.Stress; }
                    return sum / 24f;
                }
                float gloomy = Stress(Habit.Worrier, Habit.Pessimist), sunny = Stress(Habit.Optimist, Habit.TeaLover);
                Check("습관 — 걱정·비관은 더 지치고, 낙천가·차 애호가는 덜 지친다", gloomy > sunny, $"하루 평균 스트레스: 걱정+비관 {gloomy * 100:0.0}% · 낙천가+차 {sunny * 100:0.0}%");
            }
            // 6) 습관 — 부딪히는 습관 (정리광↔어지르기, 완벽주의↔서두름, 투덜이↔낙천가) · 다혈질은 더, 참을성은 덜
            {
                var w = DayOne(seed, "Hanbit");
                var a = w.Crew[0]; var b = w.Crew[1];
                var (ka, kb) = (a.Habits.ToList(), b.Habits.ToList());
                (float, string) Pair(Habit[] x, Habit[] y) { a.Habits.Clear(); a.Habits.AddRange(x); b.Habits.Clear(); b.Habits.AddRange(y); a.Value = b.Value = CrewValue.People; return LifeSystem.Clash(a, b); }
                var neat = Pair(new[] { Habit.NeatFreak }, new[] { Habit.Messy });
                var perf = Pair(new[] { Habit.Hasty }, new[] { Habit.Perfectionist });
                var grumble = Pair(new[] { Habit.Grumbler }, new[] { Habit.Optimist });
                var none = Pair(new[] { Habit.Bookworm }, new[] { Habit.Gazer });
                float hot = Persona.Of(Habit.ShortTempered).Clash, calm = Persona.Of(Habit.Patient).Clash;
                a.Habits.Clear(); a.Habits.AddRange(ka); b.Habits.Clear(); b.Habits.AddRange(kb);
                Check("습관 — 부딪히는 습관끼리 말다툼 거리가 생긴다 (다혈질은 더, 참을성은 덜)", neat.Item1 > none.Item1 && perf.Item1 > none.Item1 && grumble.Item1 > none.Item1 && hot > 1f && calm < 1f,
                    $"정리광↔어지르기 {neat.Item1:0.00} '{neat.Item2}' · 서두름↔완벽주의 {perf.Item1:0.00} '{perf.Item2}' · 투덜이↔낙천가 {grumble.Item1:0.00} '{grumble.Item2}' · 상관없음 {none.Item1:0.00} · 다혈질 ×{hot} · 참을성 ×{calm}");
            }
            // 7) 습관 — 각자 일과면 올빼미는 늦게, 아침형은 일찍 잔다
            {
                var w = DayOne(seed, "Hanbit");
                var c = w.Crew.First(x => !x.IsChild && x.Habits.All(h => Persona.Of(h).BedShift == 0f));
                float bed0 = c.Schedule.SleepStart;
                c.Habits.Add(Habit.NightOwl);
                w.Policies.Set("shifts", 0, "시험"); Run(w, SimTime.Minutes(1));
                w.Policies.Set("shifts", 2, "시험"); Run(w, SimTime.Minutes(1));
                float owl = c.Schedule.SleepStart;
                c.Habits.Remove(Habit.NightOwl); c.Habits.Add(Habit.EarlyBird);
                w.Policies.Set("shifts", 0, "시험"); Run(w, SimTime.Minutes(1));
                w.Policies.Set("shifts", 2, "시험"); Run(w, SimTime.Minutes(1));
                float lark = c.Schedule.SleepStart;
                c.Habits.Remove(Habit.EarlyBird);
                Check("습관 — 각자 일과면 올빼미는 늦게, 아침형은 일찍 잔다", MathF.Abs(SimTime.Wrap(owl - bed0) - 1.5f) < 0.01f && MathF.Abs(SimTime.Wrap(bed0 - lark) - 1.5f) < 0.01f,
                    $"{c.Name}: 보통 {bed0:0.0}시 · 올빼미 {owl:0.0}시 · 아침형 {lark:0.0}시");
            }
            // 8) 자격 20 — 새 자격이 일에 붙고, 없으면 조금 서툴다 (예전 무거운 자격보다 덜) · 솜씨가 차면 딴다
            {
                var w = DayOne(seed, "Hanbit");
                var c = w.Crew.First(x => x.CanAct && x.Role != CrewRole.Cook && !x.Quals.Contains(Qual.FoodSafety));
                var cook = new WorkOrder { Kind = WorkKind.Cook, Skill = Skill.Cooking };
                var restart = new WorkOrder { Kind = WorkKind.RestartReactor, Skill = Skill.Engineering };
                var ext = new WorkOrder { Kind = WorkKind.Extinguish, Skill = Skill.Mechanics };
                bool mapped = Life.Needs(cook) == Qual.FoodSafety && Life.Needs(ext) == Qual.Firefighting && Life.Needs(restart) == Qual.Reactor;
                var qs = c.Quals.ToList();
                c.Quals.Remove(Qual.Reactor);
                float without = w.Life.MistakeOdds(c, cook).p, withoutCore = w.Life.MistakeOdds(c, restart).p;
                c.Quals.Add(Qual.FoodSafety); c.Quals.Add(Qual.Reactor);
                float with = w.Life.MistakeOdds(c, cook).p, withCore = w.Life.MistakeOdds(c, restart).p;
                c.Quals.Clear(); foreach (var q in qs) c.Quals.Add(q);
                // 솜씨가 차면 딴다 (용접 · 배관)
                var t = w.Crew.First(x => x.CanAct && !x.Quals.Contains(Qual.Structure));
                t.SkillLevels[(int)Skill.Mechanics] = 0.75f;
                Run(w, SimTime.Hours(13));
                bool earned = t.Quals.Contains(Qual.Structure);
                bool medic = Life.HasQual(w.Crew.First(x => x.Quals.Contains(Qual.Medic)), Qual.RescueTeam);
                Check("자격 20 — 새 자격이 일에 붙고 (없으면 조금 서툴다), 솜씨가 차면 딴다 · 응급 의료는 구조대를 겸한다",
                    mapped && without > with && withoutCore / withCore > without / with && earned && medic,
                    $"조리 실수 위생 자격 없음 {without * 100:0.0}% / 있음 {with * 100:0.0}% · 원자로 재기동 없음 {withoutCore * 100:0.0}% / 있음 {withCore * 100:0.0}% · {t.Name} 골조 자격 {(earned ? "땄다" : "못 땄다")} · 배 전체 자격 {w.Crew.Sum(x => x.Quals.Count)}개");
            }
            // 9) 취미 — 쉴 때 취미의 방을 찾고, 같은 취미끼리 곁에서 쉬면 더 가까워진다
            {
                var w = DayOne(seed, "Hanbit");
                Run(w, SimTime.Hours(36));
                int rests = w.Life.Stats.HobbyRests;
                var line = w.Log.Entries.Where(e => Persona.Hobbies.Any(h => e.Text.EndsWith(h.Doing))).Select(e => e.Text).LastOrDefault();
                Check("취미 — 쉴 때 취미의 방을 찾아간다", rests > 0, $"취미로 쉰 번 {rests} · 예: {line}");
            }
            // 10) 두려움 — 무서운 일은 꺼리고 (불을 무서워하면 소화를 덜 맡는다), 그 상황이면 공황이 잦다
            {
                var w = DayOne(seed, "Mirinae");
                var c = w.Crew.First(x => x.CanAct && x.Room != null);
                var room = c.Room!;
                var cell = room.Cells.First(x => w.Ship.IsWalkable(x) && x != c.Cell);
                w.Fire.Ignite(cell, 0.5f);
                Run(w, SimTime.Minutes(1));
                var fires = w.Board.Open.Where(o => o.Kind == WorkKind.Extinguish).ToList();
                var keep = c.Fears.ToList();
                c.Fears.Clear();
                var dist = w.Paths.Flood(c.Cell, c.PathProfile);
                float calm = fires.Count > 0 ? ChoresActivity.Appeal(c, w, fires[0], dist, out _) : -9f;
                c.Fears.Add(Fear.Fire);
                float scared = fires.Count > 0 ? ChoresActivity.Appeal(c, w, fires[0], dist, out _) : -9f;
                bool trig = Persona.Triggered(w, c) == Fear.Fire;
                c.Fears.Clear(); c.Fears.AddRange(keep);
                Check("두려움 — 불이 무서우면 소화를 꺼리고, 불난 방에서 두려움이 건드려진다", fires.Count > 0 && scared < calm && trig,
                    $"소화 끌림 두려움 없음 {calm:0.00} / 불이 무섭다 {scared:0.00} · 불난 {room.Name}에서 두려움 {(trig ? "건드려짐" : "아님")}");
            }
            // 11) 두려움 — 불난 방에서 불을 무서워하는 사람은 공황 확률이 오른다 (오래가는 두려움은 평소엔 공황 없이 마음만 무겁다) · 습관도
            {
                var w = DayOne(seed, "Mirinae");
                var c = w.Crew.First(x => x.CanAct && x.Room != null);
                var room = c.Room!;
                foreach (var cell in room.Cells.Where(x => w.Ship.IsWalkable(x) && x != c.Cell).Take(3)) w.Fire.Ignite(cell, 0.6f);
                room.Air.Smoke = 0.35f; // 연기가 차기 시작했다 (위험 0.35)
                var (fears, habits, stress) = (c.Fears.ToList(), c.Habits.ToList(), c.Needs.Stress);
                c.Needs.Stress = 0.5f;
                float Rate(Fear[] fs, Habit[] hs, out Fear? f) { c.Fears.Clear(); c.Fears.AddRange(fs); c.Habits.Clear(); c.Habits.AddRange(hs); return w.Minds.PanicRate(c, out f); }
                float none = Rate(Array.Empty<Fear>(), Array.Empty<Habit>(), out _);
                float fire = Rate(new[] { Fear.Fire }, Array.Empty<Habit>(), out var why);
                float worry = Rate(Array.Empty<Fear>(), new[] { Habit.Worrier, Habit.Superstitious }, out _);
                float brave = Rate(Array.Empty<Fear>(), new[] { Habit.Daredevil, Habit.Optimist }, out _);
                // 불 없는 평범한 방: 어둠·좁은 곳은 공황 없이 (스트레스만)
                var calm = DayOne(seed + 1, "Mirinae");
                var d = calm.Crew.First(x => x.CanAct && x.Room != null);
                d.Fears.Clear(); d.Fears.Add(Fear.Confined); d.Fears.Add(Fear.Machines);
                float chronic = calm.Minds.PanicRate(d, out _);
                c.Fears.Clear(); c.Fears.AddRange(fears); c.Habits.Clear(); c.Habits.AddRange(habits); c.Needs.Stress = stress;
                // 실제로: 모두 불을 무서워하는 배에 불이 나면 두려움 때문에 공황이 일어난다
                int byFear = 0;
                for (int k = 0; k < 3; k++)
                {
                    var x = CrisisShip(seed + k, 0);
                    foreach (var m in x.Crew) { m.Fears.Clear(); m.Fears.Add(Fear.Fire); m.Needs.Stress = 0.5f; }
                    // 불난 창고에 셋이 있었다 (있기만 하면 — 불길을 보면 두려움이 건드려진다; 운에 맡기지 않게)
                    var burning = StoreRoom(x);
                    var spots = burning.Cells.Where(x.Ship.IsOpenFloor).ToList();
                    foreach (var (m, i) in x.Crew.Where(m => m.CanAct && !m.IsChild).Take(3).Select((m, i) => (m, i)))
                    {
                        m.EndJob(x, ToilStatus.Interrupted);
                        m.Position = spots[(i * 5) % spots.Count].Center; m.PreviousPosition = m.Position;
                        m.NextThinkTick = x.Tick;
                    }
                    Run(x, SimTime.Minutes(40));
                    byFear += x.Minds.FearPanics;
                }
                Check("두려움 — 불이 무서우면 불난 방에서 공황이 잦고, 오래가는 두려움은 평소엔 공황이 없다 · 습관(걱정·미신↑ 겁 없음·낙천가↓)",
                    fire > none * 1.4f && why == Fear.Fire && chronic == 0f && worry > none && brave < none && byFear > 0,
                    $"불난 {room.Name}에서 한 시간 공황 확률: 두려움 없음 {none:0.00} · 불이 무섭다 {fire:0.00} · 걱정+미신 {worry:0.00} · 겁 없음+낙천가 {brave:0.00} · 평범한 방의 좁은 곳·기계 공포 {chronic:0.00} · 실제 불 (배 셋, 40분) 두려움 공황 {byFear}번");
            }
            // 12) 말버릇 — 회의 발언에 묻어난다
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                Run(w, SimTime.Minutes(3));
                var speeches = w.Meetings.Minutes.SelectMany(m => m.Items).SelectMany(i => i.Speeches).ToList();
                var styled = speeches.Where(sp => w.Crew.FirstOrDefault(c => c.Id == sp.Who) is CrewMember c && c.Quirk >= 0
                    && Persona.Quirks[c.Quirk].Split("{0}").Where(x => x.Length > 0).All(part => sp.Text.Contains(part))).ToList();
                Check("말버릇 — 회의 발언에 묻어난다", speeches.Count > 0 && styled.Count == speeches.Count,
                    $"발언 {speeches.Count} · 말버릇 묻음 {styled.Count} · 예: " + string.Join(" / ", styled.Take(3).Select(sp => $"{w.Crew.First(c => c.Id == sp.Who).Name} “{sp.Text}”")));
            }
            // 13) 결정론
            {
                uint H()
                {
                    var w = World.CreateDefault(seed, 0, "Hanbit");
                    Run(w, SimTime.TicksPerDay + SimTime.Hours(6));
                    return SaveGame.StateHash(w);
                }
                uint x = H(), y = H();
                Check("결정론 — 개성이 든 배도 같은 시드 같은 지문", x == y, $"{x:x8} / {y:x8}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"예외: {e}");
            _fails++;
        }
        Console.WriteLine(_fails == 0 ? "\n✔ 승무원 개성 점검 모두 통과" : $"\n✘ {_fails}개 실패");
        return _fails == 0 ? 0 : 1;
    }
}
