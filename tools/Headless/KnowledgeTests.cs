using System;
using System.Linq;
using ShipSim.Core;

// v23 지식 전수 · 외부 세계 기억 점검 (--mentortest):
//   사수 · 부사수 맺기 · 날마다 배움 · 조용한 달에 크게 배우고 일지 완성 · 홀로 섬 · 잃는 솜씨 (일지 없을 때 / 있을 때) · 열네 살 부사수
//   · 향수병 (자람 · 기질 · 편지 · 기항지) · 생일 · 추모일 · 향수병 깊은 사람은 도착지에서 내린다 · 저장.
public static partial class Program
{
    private static int RunKnowledgeTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"지식 전수 · 외부 세계 기억 점검 (v23) · 시드 {seed}\n");
        try
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            GenerationSystem.QuietOff = true;
            PlanetSystem.Off = true; PurposeSystem.Off = true;
            Run(w, SimTime.Hours(7));
            var k = w.Knowledge; var h = w.Homeland; var g = w.Generation;
            var crew = w.Crew.Where(c => !c.Dead && !c.IsChild && !c.Passenger).OrderBy(c => c.Id).ToList();
            int Med = (int)Skill.Medicine, Cook = (int)Skill.Cooking;

            // 1) 사수 · 부사수
            foreach (var c in crew) c.SkillLevels[Med] = MathF.Min(c.SkillLevels[Med], 0.3f);
            var doc = crew[0];
            doc.SkillLevels[Med] = 0.85f;
            for (int i = 0; i < 12 && k.PairOf(Skill.Medicine)?.Mentor != doc.Id; i++) Run(w, SimTime.Hours(1));
            var pair = k.PairOf(Skill.Medicine);
            var app = pair == null ? null : w.Crew[pair.Apprentice];
            Check("가장 잘하는 사람이 부사수 하나를 곁에 둔다", pair != null && pair.Mentor == doc.Id && app != null && w.History.Events.Any(e => e.Text.Contains("사수가 되었다")),
                pair == null ? "짝 없음" : $"{doc.Name} → {app!.Name} ({Skills.Name(pair.Skill)}) · 짝 {k.Pairs.Count}");
            if (pair == null || app == null) throw new Exception("짝 없음");

            // 2) 날마다
            float s0 = app.RawSkill(Skill.Medicine);
            Run(w, SimTime.TicksPerDay * 3);
            Check("부사수는 날마다 조금씩 배운다", app.RawSkill(Skill.Medicine) > s0, $"{s0 * 100:0.0} → {app.RawSkill(Skill.Medicine) * 100:0.0}");

            // 3) 조용한 달: 크게 배우고 · 일지
            float s1 = app.RawSkill(Skill.Medicine);
            var sp = g.SkipMonths(6, "순항");
            var sp2 = g.SkipMonths(6, "순항");
            var man = k.ManualOf(Skill.Medicine);
            Check("조용한 달 — 부사수는 크게 배우고 · 사수는 일지를 다 쓴다 (주 컴퓨터 보관)", app.RawSkill(Skill.Medicine) - s1 >= 0.08f && man is { Ready: true } && w.History.Events.Any(e => e.Text.Contains("일지가 완성됐다")) && sp.Lines.Concat(sp2.Lines).Any(l => l.Contains("익혔다")),
                $"{s1 * 100:0} → {app.RawSkill(Skill.Medicine) * 100:0} · 일지 {(man == null ? "없음" : $"{man.Progress * 100:0}%")} · {w.History.Events.LastOrDefault(e => e.Text.Contains("일지"))?.Text}");

            // 4) 홀로 선다
            app.SkillLevels[Med] = 0.7f;
            Run(w, SimTime.Hours(2));
            Check("부사수가 홀로 서면 매듭짓는다", pair.Ended && w.History.Events.Any(e => e.Text.Contains("홀로 섰다")), pair.EndWhy);

            // 5) 잃는 솜씨 — 부사수도 일지도 없이
            var busy = k.Pairs.Where(p => !p.Ended).SelectMany(p => new[] { p.Mentor, p.Apprentice }).ToHashSet();
            var cook = crew.First(c => c != doc && c != app && !busy.Contains(c.Id));
            foreach (var c in w.Crew) c.SkillLevels[Cook] = MathF.Min(c.SkillLevels[Cook], 0.2f);
            cook.SkillLevels[Cook] = 0.62f;
            k.Manuals.RemoveAll(m => m.Skill == Skill.Cooking);
            Run(w, SimTime.Hours(2));
            Player.AllowDeath(w, true);
            cook.Vitals.Health = 0.0005f;
            for (int t = 0; t < 60 && !cook.Dead; t++) Run(w, SimTime.Minutes(1));
            Player.AllowDeath(w, false);
            Run(w, SimTime.Hours(2));
            var lost = k.Lost.LastOrDefault(l => l.Skill == Skill.Cooking);
            Check("아는 사람이 떠나고 부사수도 일지도 없으면 — 그 솜씨를 잃는다", cook.Dead && lost != null && !lost.Manual && w.History.Events.Any(e => e.Text.Contains("잃었다")),
                w.History.Events.LastOrDefault(e => e.Text.Contains("잃었다"))?.Text ?? "잃지 않음");

            // 6) 일지가 있으면 다시 배운다
            foreach (var c in w.Crew) if (c != doc) c.SkillLevels[Med] = MathF.Min(c.SkillLevels[Med], 0.2f);
            Run(w, SimTime.Hours(2));
            Player.AllowDeath(w, true);
            doc.Vitals.Health = 0.0005f;
            for (int t = 0; t < 60 && !doc.Dead; t++) Run(w, SimTime.Minutes(1));
            Player.AllowDeath(w, false);
            Run(w, SimTime.Hours(2));
            var lost2 = k.Lost.LastOrDefault(l => l.Skill == Skill.Medicine);
            float bestMed = w.Crew.Where(c => !c.Dead && !c.LeftShip).Max(c => c.RawSkill(Skill.Medicine));
            Check("일지가 남아 있으면 — 누군가 펴 들고 다시 배운다", doc.Dead && lost2 != null && lost2.Manual && bestMed >= 0.35f, $"{w.History.Events.LastOrDefault(e => e.Text.Contains("일지가 남아"))?.Text} · 가장 높은 의학 {bestMed * 100:0}");

            // 7) 열네 살
            var kid = w.Crew.First(c => !c.Dead && !c.IsChild && !c.Passenger && !k.Of(c).Any());
            int pairs0 = k.Pairs.Count(p => !p.Ended);
            k.NewAdult(kid);
            Check("열네 살이 되면 — 부사수 자리부터", k.Of(kid).Any(p => p.Apprentice == kid.Id) && w.History.Events.Any(e => e.Text.Contains("첫 부사수")), w.History.Events.LastOrDefault(e => e.Text.Contains("첫 부사수"))?.Text ?? "자리 없음");

            // 8) 향수병
            var a = w.Crew.First(c => !c.Dead && !c.LeftShip && !c.BornAboard && !c.IsChild && !c.Passenger);
            var b = w.Crew.First(c => !c.Dead && !c.LeftShip && !c.BornAboard && !c.IsChild && !c.Passenger && c != a && !Life.Has(c, Habit.Homesick));
            if (!Life.Has(a, Habit.Homesick)) a.Habits.Add(Habit.Homesick);
            var away = w.Crew.Where(c => !c.Dead && !c.LeftShip && !c.BornAboard && !c.Passenger).ToList();
            foreach (var c in away) h.Homesick[c.Id] = 0.1f;
            Run(w, SimTime.TicksPerDay * 6);
            float avg = away.Where(c => !c.Dead && !c.LeftShip).Average(h.Of);
            float ha = h.Of(a);
            h.Letter(a, News.Hello); float hl = h.Of(a);
            h.Port(); float hp = h.Of(a);
            Check("향수병 — 날마다 자라고 (향수병 기질은 더) · 편지 · 기항지가 누그러뜨린다", avg > 0.13f && HomelandSystem.GrowPerDay(a) > HomelandSystem.GrowPerDay(b) && HomelandSystem.GrowPerDay(b) > 0f && hl < ha && hp < hl,
                $"엿새 뒤 평균 {avg * 100:0}% · 하루 {HomelandSystem.GrowPerDay(a) * 100:0.0} / {HomelandSystem.GrowPerDay(b) * 100:0.0} · {a.Name} {ha * 100:0}% → 편지 {hl * 100:0}% → 기항지 {hp * 100:0}% · {h.CardLine(a)}");

            // 9) 생일
            int bd0 = h.Birthdays;
            h.BirthDay[b.Id] = (int)((g.CalendarDays + 1f) % 360f);
            Run(w, SimTime.TicksPerDay + SimTime.Hours(2));
            Check("생일 — 배의 달력으로 · 다 같이 노래를 부른다", h.Birthdays > bd0 && w.History.Events.Any(e => e.Text.Contains($"{b.Name}의 생일")), w.History.Events.LastOrDefault(e => e.Text.Contains("생일"))?.Text ?? "없음");

            // 10) 추모일 (조용히 넘긴 해 동안)
            var q1 = g.SkipMonths(6, "순항"); var q2 = g.SkipMonths(6, "순항");
            var lines = q1.Lines.Concat(q2.Lines).ToList();
            Check("떠난 지 한 해 — 추모일 · 그동안의 생일은 조촐하게", lines.Any(l => l.Contains("떠난 지 1년")) && lines.Any(l => l.Contains("생일")), string.Join(" / ", lines.Where(l => l.Contains("떠난 지") || l.Contains("생일") || l.Contains("배의 생일")).Take(3)));

            // 11) 향수병이 깊으면 도착지에서 내린다
            var home = w.Crew.Where(c => !c.Dead && !c.LeftShip && !c.BornAboard && !c.IsChild && !c.Passenger && c.Id != w.Command.CaptainId).OrderBy(c => c.Id).Take(2).ToList();
            foreach (var c in home) { h.Homesick[c.Id] = 0.95f; c.Needs.Stress = 0.1f; }
            PurposeSystem.Off = false;
            w.Purpose.BeginAs(MissionKind.Cargo);
            w.Purpose.Arrive();
            PurposeSystem.Off = true;
            var went = home.Where(c => c.LeftShip).ToList();
            Check("향수병이 아주 깊으면 — 도착지에서 내려 고향으로 간다", went.Count >= 1, went.Count == 0 ? "아무도 안 내림" : $"{string.Join(", ", went.Select(c => c.Name))} 내림 · {h.HomeOf(went[0])}");

            // 12) 저장
            var w2 = StateSave.Read(StateSave.Write(w));
            Check("저장 · 되살림 — 짝 · 일지 · 잃은 솜씨 · 향수병 · 기념일", w2.Knowledge.Pairs.Count == k.Pairs.Count && w2.Knowledge.Manuals.Count == k.Manuals.Count && w2.Knowledge.Lost.Count == k.Lost.Count && w2.Homeland.Homesick.Count == h.Homesick.Count && w2.Homeland.Kept.Count == h.Kept.Count,
                $"짝 {w2.Knowledge.Pairs.Count} · 일지 {w2.Knowledge.Manuals.Count} · 잃은 솜씨 {w2.Knowledge.Lost.Count} · 기념일 {w2.Homeland.Kept.Count}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"  ✗ 예외: {e}");
            _fails++;
        }
        GenerationSystem.QuietOff = false; PlanetSystem.Off = false; PurposeSystem.Off = false;
        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}");
        return _fails == 0 ? 0 : 1;
    }
}
