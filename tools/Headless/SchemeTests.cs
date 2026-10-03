using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ShipSim.Core;

// v18.14 승무원이 스스로 꾸미는 일 100+
public static partial class Program
{
    private static bool SchemeUntil(World w, Func<bool> done, long maxTicks, int step = 100)
    {
        for (long t = 0; t < maxTicks; t += step)
        {
            Run(w, step);
            if (done()) return true;
        }
        return done();
    }

    private static string KnowLine(World w, Scheme s) =>
        string.Join(" · ", w.Crew.Where(c => !c.Dead && !c.IsChild).Select(c => $"{c.Name}:{(s.Knows.TryGetValue(c.Id, out var k) ? k.ToString() : "모름")}"));

    private static int RunSchemeTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"승무원이 스스로 꾸미는 일 점검 (v18.14) · 시드 {seed}\n");
        string only = Environment.GetEnvironmentVariable("SHIPSIM_SCENE") ?? "";
        bool Do(string k) => only == "" || only.Contains(k);
        var all = SchemeTable.All;

        // ── 0) 표: 100가지 이상 · 일곱 갈래 · 그림이 저마다 다르다 · 개발 말투가 없다
        if (Do("0"))
        {
            Check("표 — 꾸미는 일 100가지 이상", all.Length >= 100, $"{all.Length}가지");
            Check("표 — 열쇠가 겹치지 않는다", all.Select(s => s.Key).Distinct().Count() == all.Length, "");
            Check("표 — 일곱 갈래 (장난 · 몰래 · 규칙 · 어울림 · 주고받기 · 목소리 · 혼자)", Enum.GetValues<SchemeCat>().All(c => all.Count(s => s.Cat == c) >= 8),
                string.Join(" · ", Enum.GetValues<SchemeCat>().Select(c => $"{SchemeTable.CatName(c)} {all.Count(s => s.Cat == c)}")));
            Check("표 — 그림(부품 조합)이 일마다 다르다", all.Select(s => s.Art).Distinct().Count() == all.Length, $"{all.Select(s => s.Art).Distinct().Count()}/{all.Length}");
            var bad = new[] { "AI", "시스템", "이벤트", "v1", "페일세이프", "상호작용", "트리아지", "모듈", "버그", "플레이어" };
            var leak = all.Where(s => bad.Any(b => s.Name.Contains(b) || s.Plan.Contains(b) || s.Legit.Contains(b))).Select(s => s.Key).ToList();
            Check("표 — 게임 안 글에 개발 말투가 없다", leak.Count == 0, string.Join(",", leak));
        }

        // ── 1) 밀주: 지루한 항해 중 정비사 둘이 몰래 담근다 → 냄새로 들킨다 → 회의에서 금지냐 '주점의 밤'이냐
        if (Do("1"))
        {
            var w = DayOne(seed, "Hanbit");
            w.Schemes.NoMotives = true;
            var mechs = w.Crew.Where(c => !c.Dead && !c.IsChild && c.Role is CrewRole.Engineer or CrewRole.Technician or CrewRole.Electrician).OrderBy(c => c.Id).Take(2).ToList();
            if (mechs.Count < 2) mechs = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).Take(2).ToList();
            foreach (var c in mechs) { w.Schemes.SetBored(c, 0.95f); c.Needs.Stress = 0.5f; }
            var mo = w.Schemes.Start(SchemeTable.Get("moonshine")!, mechs[0], -1, mechs[1]);
            Check("밀주 — 지루한 정비사 둘이 몰래 담그기 시작했다", mo.Stage == SchemeStage.Prep && mo.Crew.Count == 2 && w.Schemes.RoomOf(mo)?.Kind is RoomType.Engine or RoomType.Cooling or RoomType.Power or RoomType.PumpRoom or RoomType.Reactor,
                $"{mechs[0].Name}({mechs[0].Role}) · {mechs[1].Name}({mechs[1].Role}) · {w.Schemes.RoomOf(mo)?.Name}");
            int worked = 0, hid0 = w.Schemes.Stats.Hid;
            SchemeUntil(w, () => { if (mo.Working) worked++; return mo.Stage != SchemeStage.Prep; }, SimTime.TicksPerDay * 3, 50);
            Check("밀주 — 둘이 그 자리에 가서 손을 놀려 다 만들었다 (남이 오면 멈춘다)", mo.Stage != SchemeStage.Prep && worked > 0,
                $"단계 {mo.Stage} · 손 놀린 순간 {worked} · 손 멈춤 {w.Schemes.Stats.Hid - hid0} · {mo.Outcome}");
            float smelled = 0f;
            var room = w.Schemes.RoomOf(mo)!;
            bool found = SchemeUntil(w, () => { smelled = MathF.Max(smelled, w.Smells.Level(room, SmellKind.Foul)); return mo.Stage is SchemeStage.Vote or SchemeStage.Done or SchemeStage.Dropped; }, SimTime.TicksPerDay * 5, 100);
            var finder = w.Crew.FirstOrDefault(c => c.Id == mo.Finder);
            Check("밀주 — 익어 가는 술 냄새가 방에 퍼졌다", smelled > SmellSystem.Threshold(SmellKind.Foul), $"{room.Name} 냄새 {smelled:0.00}");
            Check("밀주 — 냄새를 따라온 사람이 알아챘고 반응이 갈렸다", finder != null && mo.Reactions.Count >= 1 && (mo.FoundHow.Contains("냄새") || w.Schemes.Stats.Smelled >= 1),
                $"{finder?.Name} — {mo.FoundHow} · 반응 {string.Join(" / ", mo.Reactions.Select(r => $"{w.Crew.First(c => c.Id == r.who).Name} {r.r}"))} · 냄새로 {w.Schemes.Stats.Smelled}");
            var m = w.Motions.Get(mo.Motion);
            SchemeUntil(w, () => mo.Stage is SchemeStage.Done or SchemeStage.Dropped, SimTime.TicksPerDay * 7, 200);
            var rule = w.Schemes.Rule("moonshine");
            var pr = w.Schemes.PracticeOf("moonshine");
            Check("밀주 — 회의에 올라 금지냐 '주점의 밤'이냐 표결했다", m != null && (m.Decided >= 0 || m.Stage == MotionStage.Dropped) && (rule != null),
                m == null ? $"안건 없음 · {mo.Stage} {mo.Outcome}" : $"{m.Title} [{m.Stage}] → {mo.Outcome} · 규칙 {rule?.Text} · 표 {string.Join(" ", m.Final.Select(kv => $"{w.Crew.First(c => c.Id == kv.Key).Name}{kv.Value:+0.0;-0.0}"))}");
            Check("밀주 — 결과가 배에 남았다 (관행이면 주점의 밤 · 금지면 압수한 통)", pr != null && pr.Name == "주점의 밤" || w.Schemes.Traces.Any(t => t.Scheme == mo.Id && t.State == TraceState.Seized),
                pr != null ? $"관행 {pr.Name} · {SimTime.Clock(pr.Start)} · 따르는 사람 {pr.Followers.Count}" : string.Join(" / ", w.Schemes.Traces.Select(t => $"{t.Text}({t.State})")));
            Check("밀주 — 일기에 남았다", mechs[0].Diary.Any(d => d.text.Contains("술") || d.text.Contains("밀주")), mechs[0].Diary.LastOrDefault().text ?? "");
        }

        // ── 2) 장난: 들키면 관계가 바뀐다 — 웃어넘기는 사람 · 앙금이 남는 사람 (성격 따라)
        if (Do("2"))
        {
            var w = DayOne(seed, "Hanbit");
            w.Schemes.NoMotives = true;
            var crew = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).ToList();
            var joker = crew[0];
            joker.Habits.Add(Habit.Prankster);
            var easy = crew[1]; var touchy = crew[2];
            easy.Habits.RemoveAll(h => h is Habit.Serious or Habit.ShortTempered or Habit.Grumbler); easy.Habits.Add(Habit.Cheerful); easy.Needs.Stress = 0.1f;
            touchy.Habits.RemoveAll(h => h is Habit.Cheerful or Habit.Joker or Habit.Prankster); touchy.Habits.Add(Habit.Serious); touchy.Habits.Add(Habit.ShortTempered); touchy.Needs.Stress = 0.75f;
            float a0 = easy.AffinityTo(joker), b0 = touchy.AffinityTo(joker);
            var p1 = w.Schemes.Start(SchemeTable.Get("cushion_trap")!, joker, easy.Id);
            var p2 = w.Schemes.Start(SchemeTable.Get("salt_sugar")!, joker, touchy.Id);
            // 하나는 저절로: 놓아두고 걸릴 사람이 오기를 기다린다
            SchemeUntil(w, () => p1.Stage != SchemeStage.Prep, SimTime.TicksPerDay, 50);
            SchemeUntil(w, () => p1.Over(), SimTime.TicksPerDay, 50);
            if (!p1.Over()) w.Schemes.Spring(p1, easy, true);
            w.Schemes.Spring(p2, touchy, true);
            Check("장난 — 놓아둔 장난이 걸릴 사람에게 터졌다", p1.Over() && p2.Over(), $"{p1.Spec.Name}: {p1.Outcome} / {p2.Spec.Name}: {p2.Outcome}");
            var mem1 = w.Relations.Why(easy, joker); var mem2 = w.Relations.Why(touchy, joker);
            Check("장난 — 잘 웃는 사람은 웃어넘겼다 (사이가 좋아졌다)", p1.Outcome.Contains("웃") && easy.AffinityTo(joker) > a0, $"{easy.Name}: {p1.Outcome} · 호감 {a0:0.00}→{easy.AffinityTo(joker):0.00} · {mem1?.Text}");
            Check("장난 — 예민한 사람은 앙금이 남았다 (사이가 나빠졌다)", p2.Outcome.Contains("앙금") && touchy.AffinityTo(joker) < b0, $"{touchy.Name}: {p2.Outcome} · 호감 {b0:0.00}→{touchy.AffinityTo(joker):0.00} · {mem2?.Text}");
            // 누가 했는지 모르면 엉뚱한 사람을 의심할 수 있다
            var p3 = w.Schemes.Start(SchemeTable.Get("glued_cup")!, joker, touchy.Id);
            w.Schemes.Spring(p3, touchy, false);
            Check("장난 — 누가 했는지 모르면 메신저에 묻고 짐작한다", !p3.Identified && w.Info.Chat.All.Any(m => m.Author == touchy.Id && m.Kind == ChatKind.Ask), $"{p3.Outcome}");
        }

        // ── 3) 해적 방송: 하는 사람 · 들은 사람(누가 하는지는 모른다) · 자느라 모르는 사람
        if (Do("3"))
        {
            var w = DayOne(seed, "Hanbit");
            w.Schemes.NoMotives = true;
            var crew = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).ToList();
            var dj = crew[3]; var mate = crew[4];
            var r = w.Schemes.Start(SchemeTable.Get("pirate_radio")!, dj, -1, mate);
            SchemeUntil(w, () => r.Sessions >= 1 || r.Over(), SimTime.TicksPerDay * 4, 100);
            // 첫 방송 전에 들켜 끝났으면 (그것도 이 배에서 일어나는 일이다) 둘이 다시 판을 벌인다
            for (int i = 0; i < 2 && r.Sessions == 0 && r.Over(); i++)
            {
                Console.WriteLine($"   첫 방송 전에 끝났다: {r.Outcome}");
                r = w.Schemes.Start(SchemeTable.Get("pirate_radio")!, dj, -1, mate);
                SchemeUntil(w, () => r.Sessions >= 1 || r.Over(), SimTime.TicksPerDay * 4, 100);
            }
            int part = r.Knows.Count(k => k.Value == KnowHow.Part), heard = r.Knows.Count(k => k.Value == KnowHow.Heard);
            int none = crew.Count(c => !r.Knew(c.Id));
            Check("해적 방송 — 밤 방송이 나갔다", r.Sessions >= 1, $"{r.Stage} · 모임 {r.Sessions} · {r.Outcome}");
            Check("해적 방송 — 아는 사람(하는 사람 · 들은 사람)과 모르는 사람이 갈렸다", part >= 2 && heard >= 1 && none >= 1, KnowLine(w, r));
            Check("해적 방송 — 들은 사람은 누가 하는지 모른다", crew.Where(c => r.Knows.TryGetValue(c.Id, out var k) && k == KnowHow.Heard).All(c => !r.KnowsWho(c.Id)), "");
            Run(w, SimTime.TicksPerDay);
            Check("해적 방송 — 아침에 메신저로 소문이 돈다 · 컴퓨터는 회선을 본다", w.Info.Chat.All.Any(m => m.Text.Contains("방송")) || r.ComputerKnows,
                $"컴퓨터 {(r.ComputerKnows ? $"앎({r.ComputerSaid} · {r.ComputerWhy})" : "모름")} · {r.Outcome}");
        }

        // ── 4) 비밀 정원 → 들킨다 → 공용 정원
        if (Do("4"))
        {
            var w = DayOne(seed, "Hanbit");
            w.Schemes.NoMotives = true;
            var crew = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).ToList();
            var gardener = crew[5]; gardener.Hobbies.Add(Hobby.Gardening);
            var g = w.Schemes.Start(SchemeTable.Get("secret_garden")!, gardener);
            w.Schemes.SetBored(gardener, 0.8f); // 고향 생각 · 지루함이 정원을 꾸미게 한다
            SchemeUntil(w, () => g.Stage != SchemeStage.Prep, SimTime.TicksPerDay * 8, 100);
            Check("비밀 정원 — 창고 구석에서 몰래 키웠다", g.Stage is SchemeStage.Live or SchemeStage.Done, $"{w.Schemes.RoomOf(g)?.Name} · {g.Stage} · 진척 {g.Progress:0.00} · 손 멈춤 {w.Schemes.Stats.Hid} · 지루함 {w.Schemes.Bored(gardener):0.00} · {g.Outcome}");
            Run(w, SimTime.Hours(2));
            // 다른 사람이 먼저 보고 일러 치워졌으면 (그것도 이 배에서 일어나는 일이다) 한 번 더 키운다
            if (!g.Active) { Console.WriteLine($"   먼저 들켰다: {g.Outcome}"); g = w.Schemes.Start(SchemeTable.Get("secret_garden")!, gardener); }
            var finder = crew.Where(c => c != gardener && !g.KnowsWho(c.Id)).OrderByDescending(c => w.Schemes.Approve(c, g).v).First();
            if (g.Active) w.Schemes.Discover(g, finder, "창고에 갔다가 봤다");
            var tr = w.Schemes.Traces.FirstOrDefault(t => t.Scheme == g.Id);
            Check("비밀 정원 — 들키고 나서 모두의 정원이 됐다", tr is { State: TraceState.Public } && w.Schemes.Stats.Adopted >= 1, $"{finder.Name} 찬반 {w.Schemes.Approve(finder, g).v:+0.00} · {g.Outcome} · {tr?.Text}({tr?.State})");
            float s0 = gardener.Needs.Stress;
            Check("비밀 정원 — 메신저로 알렸다 (읽은 사람은 안다)", w.Info.Chat.All.Any(m => m.Text.Contains("공용 정원")), "");
        }

        // ── 5) 동호회가 생겨 관행이 된다
        if (Do("5"))
        {
            var w = DayOne(seed, "Hanbit");
            w.Schemes.NoMotives = true;
            var crew = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).ToList();
            foreach (var c in crew.Take(5)) if (!c.Hobbies.Contains(Hobby.Chess)) c.Hobbies.Add(Hobby.Chess);
            var club = w.Schemes.Start(SchemeTable.Get("chess_club")!, crew[0]);
            SchemeUntil(w, () => club.Over(), SimTime.TicksPerDay * 6, 200);
            var pr = w.Schemes.PracticeOf("chess_club");
            Check("동호회 — 메신저로 사람을 모아 모임을 이어 갔다", club.Sessions >= 3, $"{club.Stage} · 모임 {club.Sessions} (약함 {club.Weak}) · 회원 {club.Crew.Count} · {club.Outcome}");
            Check("동호회 — 세 번 넘게 이어지자 배의 관행이 됐다", pr != null && pr.Followers.Count >= 3, pr != null ? $"{pr.Name} · {pr.Every}일마다 {pr.Hour}시 · 따르는 사람 {pr.Followers.Count}" : "없음");
            if (pr != null)
            {
                SchemeUntil(w, () => pr.Held >= 1, SimTime.TicksPerDay * 3, 200);
                Check("동호회 — 관행이 된 뒤에도 사람들이 모인다", pr.Held >= 1, $"열린 횟수 {pr.Held} · 마지막 {pr.LastTurnout}명");
            }
        }

        // ── 6) 도박판 → 빚 → 다툼
        if (Do("6"))
        {
            var w = DayOne(seed, "Hanbit");
            w.Schemes.NoMotives = true;
            var crew = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).ToList();
            foreach (var c in crew.Take(3)) { c.Habits.Add(Habit.ShortTempered); if (!c.Hobbies.Contains(Hobby.Cards)) c.Hobbies.Add(Hobby.Cards); }
            var den = w.Schemes.Start(SchemeTable.Get("gambling_den")!, crew[0], -1, crew[1], crew[2]);
            SchemeUntil(w, () => w.Schemes.Stats.Quarrels >= 1 || den.Over() && w.Schemes.Debts.All(d => d.Amount < 3), SimTime.TicksPerDay * 8, 200);
            // 빚이 커지기 전에 판이 막혔으면 (그것도 이 배에서 일어나는 일이다) 몰래 다시 판을 벌인다
            for (int i = 0; i < 2 && w.Schemes.Stats.Quarrels == 0 && den.Over(); i++)
            {
                Console.WriteLine($"   빚이 커지기 전에 끝났다: {den.Outcome}");
                den = w.Schemes.Start(SchemeTable.Get("gambling_den")!, crew[0], -1, crew[1], crew[2]);
                SchemeUntil(w, () => w.Schemes.Stats.Quarrels >= 1 || den.Over() && w.Schemes.Debts.All(d => d.Amount < 3), SimTime.TicksPerDay * 8, 200);
            }
            Check("도박판 — 밤마다 판이 벌어져 빚이 생겼다", den.Sessions >= 1 && w.Schemes.Debts.Count >= 1,
                $"판 {den.Sessions} · 빚 {string.Join(" / ", w.Schemes.Debts.Select(d => $"{w.Crew.First(c => c.Id == d.From).Name}→{w.Crew.First(c => c.Id == d.To).Name} {d.Amount}"))}");
            Check("도박판 — 빚 때문에 다퉜다 (관계 · 감정이 남는다)", w.Schemes.Stats.Quarrels >= 1 && w.Relations.All.Any(m => m.Reason == RelationReason.OwesMe),
                $"다툼 {w.Schemes.Stats.Quarrels} · {den.Stage} {den.Outcome}");
        }

        // ── 7) 배급 빼돌리기: 컴퓨터가 장부 대조로 알아챈다 → 성격 · 방침에 따라 함장에게 알리거나 안 알린다
        if (Do("7"))
        {
            (Scheme s, World w) Skim(float caution, float people, int privacy)
            {
                var w = DayOne(seed, "Hanbit");
                w.Schemes.NoMotives = true;
                w.Automation.Character.Nudge(caution, people, "시험");
                w.Policies.Set("privacy", privacy, "시험");
                var thief = w.Crew.Where(c => !c.Dead && !c.IsChild && c.Id != w.Command.CaptainId).OrderBy(c => c.Id).Skip(2).First();
                thief.Value = CrewValue.Freedom;
                var s = w.Schemes.Start(SchemeTable.Get("ration_skim")!, thief);
                SchemeUntil(w, () => s.ComputerKnows || s.Over(), SimTime.TicksPerDay * 4, 200);
                // 컴퓨터가 맞춰 보기 전에 사람이 먼저 봤으면 (그것도 이 배에서 일어나는 일이다) 다른 사람이 또 빼돌린다
                for (int i = 0; i < 2 && !s.ComputerKnows && s.Skimmed == 0; i++)
                {
                    Console.WriteLine($"   사람이 먼저 봤다: {w.Crew.FirstOrDefault(c => c.Id == s.Finder)?.Name}({s.FoundHow}) · {s.Stage}");
                    thief = w.Crew.Where(c => !c.Dead && !c.IsChild && c.Id != w.Command.CaptainId && !s.Knew(c.Id)).OrderBy(c => c.Id).First();
                    thief.Value = CrewValue.Freedom;
                    s = w.Schemes.Start(SchemeTable.Get("ration_skim")!, thief);
                    SchemeUntil(w, () => s.ComputerKnows || s.Over(), SimTime.TicksPerDay * 4, 200);
                }
                Console.WriteLine($"   빼돌리기 [{caution:+0.0;-0.0}]: {s.Stage} 진척 {s.Progress:0.00} · 빼돌림 {s.Skimmed} · 찾은 사람 {w.Crew.FirstOrDefault(c => c.Id == s.Finder)?.Name}({s.FoundHow}) · 창고 비상식량 {w.Ship.CountStored(ItemKind.Ration)} 식사 {w.Ship.CountStored(ItemKind.Meal)} 채소 {w.Ship.CountStored(ItemKind.Produce)} · {s.Outcome}");
                return (s, w);
            }
            var (sa, wa) = Skim(0.8f, -0.6f, 0);
            var (sb, wb) = Skim(-0.6f, 0.8f, 1);
            Check("컴퓨터 — 장부와 실제 재고가 어긋난 걸 알아챘다", sa.ComputerKnows && sb.ComputerKnows && sa.Skimmed >= 2, $"빼돌린 비상식량 {sa.Skimmed} / {sb.Skimmed}");
            Check("컴퓨터 — 신중 · 배 우선이면 함장에게 알린다", sa.ComputerSaid == 1, $"{wa.Automation.Character.Line} → {sa.ComputerSaid} ({sa.ComputerWhy}) · {sa.Stage} {sa.Outcome}");
            Check("컴퓨터 — 과감 · 사람 우선 · 사생활 존중이면 함장에게는 말하지 않는다", sb.ComputerSaid is 0 or 2, $"{wb.Automation.Character.Line} → {sb.ComputerSaid} ({sb.ComputerWhy}) · {sb.Outcome}");
            var cap = wa.Command.Captain;
            Check("컴퓨터 — 함장이 들은 뒤 승무원이 움직였다 (고발 · 재판)", cap != null && sa.KnowsWho(cap.Id) && (sa.Motion >= 0 || sa.Over()), $"{sa.Stage} · 안건 {wa.Motions.Get(sa.Motion)?.Title}");
            Check("컴퓨터 — 함장은 컴퓨터가 알려 준 것만 안다 (다른 쪽 함장은 컴퓨터에게서 듣지 못했다)", sb.ComputerSaid != 1 && (wb.Command.Captain is not CrewMember cb || !sb.Knows.TryGetValue(cb.Id, out var kb) || kb != KnowHow.Told || sb.Crew.Contains(cb.Id)), KnowLine(wb, sb));
        }

        // ── 8) 저절로: 열흘 항해에서 여러 가지 일이 저마다 다른 사람에게서 나온다
        if (Do("8"))
        {
            var w = World.CreateDefault(seed, 0, "Hanbit");
            Run(w, SimTime.TicksPerDay * 10);
            var st = w.Schemes.Stats;
            var kinds = w.Schemes.Tried.Keys.ToList();
            var cats = kinds.Select(k => SchemeTable.Get(k)!.Cat).Distinct().Count();
            var leads = w.Schemes.All.Select(s => s.Lead).Distinct().Count();
            Console.WriteLine($"   열흘: {st.Line()}");
            Console.WriteLine($"   해 본 일 {kinds.Count}가지: {string.Join(", ", kinds.Select(k => SchemeTable.Get(k)!.Name))}");
            Check("저절로 — 열흘 동안 여러 가지 일을 꾸몄다", kinds.Count >= 10 && cats >= 4 && leads >= 4, $"{kinds.Count}가지 · {cats}갈래 · 꾸민 사람 {leads}명");
            Check("저절로 — 들키기도 하고 끝까지 가기도 한다", st.Found >= 2 && w.Schemes.All.Count(s => s.Over()) >= 4, $"들킴 {st.Found} · 끝남 {w.Schemes.All.Count(s => s.Over())}");
            Check("저절로 — 결과가 배에 남는다 (관행 · 규칙 · 흔적)", w.Schemes.Traces.Count + w.Schemes.Rules.Count + w.Schemes.Practices.Count >= 3,
                $"흔적 {w.Schemes.Traces.Count} · 규칙 {w.Schemes.Rules.Count} · 관행 {string.Join(", ", w.Schemes.Practices.Select(p => p.Name))}");
        }

        // ── 9) 결정론 · 성능
        if (Do("9"))
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint h1 = H(), h2 = H();
            Check("결정론 (같은 시드 · 같은 지문)", h1 == h2, $"{h1:x8} / {h2:x8}");
            if (Environment.GetEnvironmentVariable("SHIPSIM_NOPERF") != "1")
            {
                double Day(bool off)
                {
                    SchemeSystem.Off = off;
                    var w = World.CreateDefault(seed, 0, "Cheonma");
                    Run(w, SimTime.Hours(2));
                    SchemeSystem.UpdateTicks = 0;
                    var sw = Stopwatch.StartNew();
                    Run(w, SimTime.TicksPerDay);
                    SchemeSystem.Off = false;
                    return sw.Elapsed.TotalSeconds;
                }
                double t0 = Math.Min(Day(true), Day(true)), t1 = Math.Min(Day(false), Day(false));
                double own = SchemeSystem.UpdateTicks * 1.0 / Stopwatch.Frequency;
                Console.WriteLine($"   성능 (30인 하루): 끔 {t0:0.0}초 · 켬 {t1:0.0}초 ({(t1 / t0 - 1) * 100:+0;-0}%) · 이 시스템 틱 {own * 1000:0}ms");
                Check("성능 · 30인 배 하루 ±10%", t1 < t0 * 1.10, $"{t0:0.0} → {t1:0.0}초");
            }
        }

        Console.WriteLine(_fails == 0 ? "\n모두 통과" : $"\n실패 {_fails}건");
        return _fails == 0 ? 0 : 1;
    }
}
