using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using ShipSim.Core;

// v18.16 개인 이야기 아크 · v18.17 대화 카드 · 캠프의 밤 · 잡담 · 로맨스 점검 (--talestest).
public static partial class Program
{
    private static readonly string[] TaleBad = { "AI", "시스템", "이벤트", "v1", "페일세이프", "상호작용", "트리아지", "모듈", "버그", "플레이어", "선장", "확률", "아크", "카드" };

    private static bool TaleUntil(World w, Func<bool> done, long maxTicks, int step = 150)
    {
        for (long t = 0; t < maxTicks; t += step) { Run(w, step); if (done()) return true; }
        return done();
    }

    private static void Bond(CrewMember a, CrewMember b, float v) { a.Affinity[b.Id] = v; b.Affinity[a.Id] = v; }

    private static int RunTalesTest(int seed)
    {
        _fails = 0;
        Console.WriteLine($"개인 이야기 · 대화 카드 · 캠프의 밤 · 잡담 · 로맨스 점검 (v18.16 · v18.17) · 시드 {seed}\n");
        string only = Environment.GetEnvironmentVariable("SHIPSIM_SCENE") ?? "";
        bool Do(string k) => only == "" || only.Contains(k);
        var all = StoryTable.All;

        // ── 0) 표: 틀 20가지 이상 · 단계 3~5 · 갈래가 이어진다 · 그림이 저마다 · 게임 안 글에 개발 말투가 없다
        if (Do("0"))
        {
            Check("표 — 이야기 틀 20가지 이상", all.Length >= 20, $"{all.Length}가지 · " + string.Join(" · ", Enum.GetValues<ArcTheme>().Select(t => $"{StoryTable.ThemeName(t)} {all.Count(s => s.Theme == t)}")));
            Check("표 — 열쇠가 겹치지 않는다", all.Select(s => s.Key).Distinct().Count() == all.Length, "");
            Check("표 — 단계가 3~5 (시작 · 갈등 · 결말)", all.All(s => s.Steps.Length is >= 3 and <= 5), string.Join(",", all.Where(s => s.Steps.Length is < 3 or > 5).Select(s => s.Key)));
            Check("표 — 실패 갈래가 표 안의 틀로 이어진다", all.Where(s => s.Branch != "").All(s => StoryTable.Get(s.Branch) != null) && all.Count(s => s.Branch != "") >= 12, $"{all.Count(s => s.Branch != "")}개");
            Check("표 — 그림(상징)이 틀마다 다르다", all.Select(s => s.Emblem).Distinct().Count() == all.Length, $"{all.Select(s => s.Emblem).Distinct().Count()}/{all.Length}");
            var texts = all.SelectMany(s => s.Steps.Select(x => x.Text).Concat(new[] { s.Name, s.Win, s.Lose, s.Ruin })).ToList();
            var leak = texts.Where(t => TaleBad.Any(b => t.Contains(b))).ToList();
            Check("표 — 게임 안 글에 개발 말투 · '선장'이 없다", leak.Count == 0, string.Join(" / ", leak.Take(3)));
            Check("표 — 장소 · 털어놓기 · 배가 다침 · 다툼 · 죽음이 단계를 연다", Enum.GetValues<ArcGate>().All(g => all.Any(s => s.Steps.Any(x => x.Gate == g))), "");
        }

        // ── 1) 긴 항해: 한 사람의 이야기가 시작 → 갈등 → 결말 · 결말이 다른 사람의 이야기를 민다 · 컴퓨터가 알아챈다
        if (Do("1"))
        {
            var w = DayOne(seed, "Hanbit");
            var st = w.Tales;
            st.NoSeeds = true; st.Arcs.Clear(); st.Pace = 6f;
            var adults = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).ToList();
            var hero = adults[0]; var friend = adults[1];
            Bond(hero, friend, 0.65f);
            var arc = st.Start(StoryTable.Get("hidden_tremor")!, hero);
            Arc? farc = null;
            Check("이야기 — 틀에 그 사람의 조각이 끼워졌다", arc.Beats.Count == 1 && !arc.Beats[0].text.Contains('{'), arc.Beats[0].text);
            int walked = 0, visitedMed = 0;
            bool done = TaleUntil(w, () =>
            {
                if (hero.Job?.Activity is StoryActivity) walked++;
                if (hero.Room?.Kind is RoomType.Medbay or RoomType.Triage) visitedMed++;
                if (farc == null && arc.Step >= 2) farc = st.Start(StoryTable.Get("write_book")!, friend); // 그 사이 친구에게도 이야기가 생겼다
                return !arc.Active;
            }, SimTime.TicksPerDay * 4);
            Check("이야기 — 시작 → 갈등 → 결말까지 갔다", done && arc.Beats.Count >= arc.Spec.Steps.Length && arc.End != ArcEnd.None,
                $"{arc.Title}: 단계 {arc.Beats.Count}/{arc.Spec.Steps.Length} · {StoryTable.EndName(arc.End)} — {arc.Ending}");
            foreach (var b in arc.Beats) Console.WriteLine($"     · {SimTime.Clock(b.t)} {b.text}");
            Check("이야기 — 승무원이 이야기대로 움직였다 (의무실 · 털어놓으러 감)", walked > 0 && (visitedMed > 0 || st.Stats.Visits > 0), $"이야기 일 {walked}틱 · 의무실 {visitedMed}틱 · 찾아감 {st.Stats.Visits}");
            var heart = st.Cards.FirstOrDefault(k => k.Kind == CardKind.Heart && k.Speaker == hero.Id);
            Check("이야기 — 털어놓기는 대화 카드로 (가까운 사람에게)", heart != null || arc.Confided, heart != null ? $"{heart.Pick?.Text} → {heart.Outcome}" : "밤 모임에서 털어놓음");
            Check("이야기 — 주 컴퓨터가 기록으로 먼저 알아채고 조용히 권했다", arc.ComputerSaw && st.Stats.ComputerNotes > 0, $"알림 {st.Stats.ComputerNotes}");
            bool touched = farc != null && farc.Touched.Any(t => t.who == hero.Id) || st.Arcs.Any(x => x.From == arc.Id);
            Check("이야기 — 결말이 다른 사람 이야기에 번졌다", touched, string.Join(" · ", farc?.Touched.Select(t => t.what) ?? Array.Empty<string>()) + " " + string.Join(" · ", st.Arcs.Where(x => x.From == arc.Id).Select(x => $"{x.Title} 시작")));
            if (arc.End == ArcEnd.Failed) Check("이야기 — 실패는 새 갈래로", st.Arcs.Any(x => x.From == arc.Id && x.Who == hero.Id), "");
            // 비극은 털어놓은 사람에게 새 이야기를 연다 (꾸며서라도)
            var w2 = DayOne(seed, "Hanbit");
            var s2 = w2.Tales; s2.NoSeeds = true; s2.Arcs.Clear();
            var a2 = w2.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).ToList();
            Bond(a2[2], a2[3], 0.6f);
            var t2 = s2.Start(StoryTable.Get("sick_parent")!, a2[2]);
            t2.Friend = a2[3].Id; t2.Confided = true;
            s2.Finish(t2, a2[2], ArcEnd.Tragic);
            var sg = s2.Arcs.FirstOrDefault(x => x.Who == a2[3].Id && x.From == t2.Id);
            Check("이야기 — 비극의 결말이 털어놓은 사람의 새 이야기를 연다", sg != null, sg != null ? $"{a2[3].Name}: {sg.Title} — {sg.Beats[0].text}" : "");
            s2.Finish(s2.Start(StoryTable.Get("gambling_debt")!, a2[4]), a2[4], ArcEnd.Failed);
            Check("이야기 — 실패도 새 갈래 (빚 → 빚쟁이의 그림자)", s2.Arcs.Any(x => x.Who == a2[4].Id && x.Spec.Key == "debt_collector" && x.Active), "");
        }

        // ── 2) 반란 직전 설득: 깊은 사이만 열리는 선택지로 진정시킨다 · 얕은 사이의 실패는 새 갈래 · 저절로 찾아가 말린다
        if (Do("2"))
        {
            var w = DayOne(seed, "Hanbit");
            var st = w.Tales; st.NoSeeds = true;
            var cap = w.Command.Captain;
            var pool = w.Crew.Where(c => !c.Dead && !c.IsChild && c.Id != w.Command.CaptainId).OrderBy(c => c.Id).ToList();
            var lead = pool[0]; var ally = pool[1]; var deep = pool[2]; var thin = pool[3];
            Bond(deep, lead, 0.75f); Bond(thin, lead, 0.02f);
            var mp = SchemeTable.Get("mutiny_plot")!;
            var plot = w.Schemes.Start(mp, lead, cap?.Id ?? -1, ally);
            var k1 = st.Hold(CardKind.Persuade, deep, lead, plot.Id, roll: 0.2f);
            var bond = k1.Options.First(o => o.Key == "bond");
            Check("설득 — 깊은 사이에만 '옛정에 기댄다'가 열린다", bond.Open, $"{deep.Name}↔{lead.Name} {deep.AffinityTo(lead):0.00}");
            Console.WriteLine($"     카드: {k1.Title} · {k1.Scene}");
            foreach (var o in k1.Options) Console.WriteLine($"       {(o == k1.Pick ? "▶" : " ")} {o.Text} {(o.Open ? $"{o.Chance * 100:0}% ({string.Join(" · ", o.Why.Select(x => $"{x.label} {x.v * 100:+0;-0}"))})" : $"[닫힘: {o.Lock}]")}");
            Check("설득 — 말하는 사람이 성격대로 골랐다 (그 선택지 · 이유가 남는다)", k1.Pick == bond && k1.ChoseWhy != "", k1.ChoseWhy);
            Check("설득 — 가능성을 근거(솜씨 · 관계 · 기분 · 상황)와 함께 셈했다", bond.Why.Count == 4 && bond.Why.Any(x => x.label == "관계" && x.v > 0.15f), string.Join(" · ", bond.Why.Select(x => $"{x.label} {x.v:+0.00;-0.00}")));
            Check("설득 — 진정시켰다: 모의를 접었다", k1.Success && plot.Stage == SchemeStage.Dropped, $"{k1.Outcome} · 모의 {plot.Stage} · “{k1.Answer}”");
            Check("설득 — 일기 · 연대기에 남았다", lead.Diary.Any(d => d.text.Contains(deep.Name)) && w.History.Events.Any(e => e.Text.Contains("반란 설득")), "");
            var plot2 = w.Schemes.Start(mp, lead, cap?.Id ?? -1, ally);
            var k2 = st.Hold(CardKind.Persuade, thin, lead, plot2.Id, roll: 0.99f);
            var bond2 = k2.Options.First(o => o.Key == "bond");
            Check("설득 — 얕은 사이에는 그 선택지가 닫혀 있다 (이유와 함께)", !bond2.Open && bond2.Lock != "", bond2.Lock);
            Check("설득 — 실패한 설득이 새 갈래가 됐다", !k2.Success && k2.Branch != "", $"{k2.Pick?.Text} → {k2.Outcome} · {k2.Branch} · 모의 진척 {plot2.Progress:0.00} · 함장 앎 {(cap != null && plot2.Knows.ContainsKey(cap.Id))}");
            // 저절로: 우두머리의 가까운 사람이 낌새를 채고 찾아가 말린다
            plot2.Stage = SchemeStage.Dropped;
            var plot3 = w.Schemes.Start(mp, lead, cap?.Id ?? -1, ally);
            bool went = TaleUntil(w, () => st.Cards.Any(k => k.Kind == CardKind.Persuade && k.Ref == plot3.Id), SimTime.Hours(14), 60);
            var k3 = st.Cards.LastOrDefault(k => k.Ref == plot3.Id && k.Kind == CardKind.Persuade);
            Check("설득 — 가까운 사람이 저절로 찾아가 카드를 펼쳤다", went, k3 != null ? $"{w.Crew.First(c => c.Id == k3.Speaker).Name} → {lead.Name}: {k3.Pick?.Text} ({k3.Pick?.Chance * 100:0}%) — {k3.Outcome}" : $"모의 {plot3.Stage}");
        }

        // ── 3) 잡담: 같은 사건을 두고 조합마다 다른 말 · 스쳐 지나가며 저절로
        if (Do("3"))
        {
            var w = DayOne(seed, "Hanbit");
            var st = w.Tales;
            var p = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).Take(5).ToList();
            Bond(p[0], p[1], 0.5f); Bond(p[0], p[2], -0.35f);
            string ev = "산소 탱크가 샜다";
            var pairs = new[] { (p[0], p[1]), (p[0], p[2]), (p[1], p[2]), (p[3], p[4]), (p[1], p[3]) };
            var lines = pairs.Select(x => st.Passing(x.Item1, x.Item2, ev)).ToList();
            for (int i = 0; i < pairs.Length; i++) Console.WriteLine($"     {pairs[i].Item1.Name}({StorySystem.HomeShort[st.RootsOf(pairs[i].Item1).Home]}) → {pairs[i].Item2.Name}({StorySystem.HomeShort[st.RootsOf(pairs[i].Item2).Home]}): “{lines[i].a}” / “{lines[i].b}”");
            Check("잡담 — 같은 사건이라도 조합마다 다른 말", lines.Select(x => x.a + "|" + x.b).Distinct().Count() == pairs.Length, $"{lines.Select(x => x.a + x.b).Distinct().Count()}/{pairs.Length}");
            Check("잡담 — 사이가 나쁘면 말이 차갑다", lines[1].a.Contains("상관없") || lines[1].b.Contains("지나갈게"), $"{lines[1].a} / {lines[1].b}");
            Check("잡담 — 출신(고향 · 집안 · 세대 · 믿음)이 사람마다 정해져 있다", p.Select(c => st.RootsLine(c)).Distinct().Count() >= 3, string.Join(" | ", p.Take(3).Select(c => st.RootsLine(c))));
            int n0 = st.Stats.SmallTalks;
            Run(w, SimTime.TicksPerDay);
            var topics = st.Chatter.Select(c => c.topic.Split(':')[0]).Distinct().ToList();
            Check("잡담 — 스쳐 지나가며 저절로 나눈다 (수다 자리와 따로)", st.Stats.SmallTalks > n0 && topics.Count >= 2, $"{st.Stats.SmallTalks - n0}번 · 거리: {string.Join(" · ", topics)}");
        }

        // ── 4) 캠프의 밤: 하루 끝에 모여 쌓인 일로 장면 (다툼 · 털어놓기 · 고향 이야기)
        if (Do("4"))
        {
            var w = DayOne(seed, "Hanbit");
            var st = w.Tales; st.NoSeeds = true;
            var p = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).ToList();
            Bond(p[0], p[1], -0.4f);
            p[0].Quarrel = p[1].Quarrel = w.Tick;
            int before = st.Camps.Count;
            bool camp = TaleUntil(w, () => st.Camps.Count > before && st.Camps[^1].Closed, SimTime.TicksPerDay + SimTime.Hours(4), 200);
            var cn = st.Camps.LastOrDefault();
            var room = cn == null ? null : w.Ship.Rooms.FirstOrDefault(r => r.Id == cn.RoomId);
            Check("캠프의 밤 — 하루 끝에 사람들이 식당 · 휴게실에 모였다", camp && cn!.Came.Count >= 3 && room?.Kind is RoomType.Mess or RoomType.Lounge or RoomType.Galley, $"{room?.Name} · {cn?.Came.Count}명");
            Check("캠프의 밤 — 그날 쌓인 일로 장면이 이어졌다", cn != null && cn.Scenes.Count >= 2, string.Join(" / ", cn?.Scenes.Select(s => s.text) ?? Array.Empty<string>()));
            Check("캠프의 밤 — 연대기에 남았다", w.History.Events.Any(e => e.Text.StartsWith("밤 모임")), "");
        }

        // ── 5) 로맨스: 호감 → 고백(카드) → 연인 → 위기 · 질투 → 고비 카드 · 주변 반응
        if (Do("5"))
        {
            var w = DayOne(seed, "Hanbit");
            var st = w.Tales; st.NoSeeds = true; st.Pace = 8f; st.Loves.Clear();
            var p = w.Crew.Where(c => !c.Dead && !c.IsChild && c.Partner == null && c.Id != w.Command.CaptainId).OrderByDescending(c => c.Traits.Sociability).ThenBy(c => c.Id).ToList();
            var a = p[0]; var b = p[1]; var third = p[2];
            Bond(a, b, 0.62f);
            var l = st.Crush(a, b);
            Check("로맨스 — 호감: 마음이 간 사람이 그 사람 곁을 찾는다", l.Stage == LoveStage.Crush && w.Brain2.Goals.Has(a, "love:near"), "");
            bool conf = TaleUntil(w, () => st.Cards.Any(k => k.Kind == CardKind.Confess && k.Ref == l.Id), SimTime.TicksPerDay, 60);
            var kc = st.Cards.LastOrDefault(k => k.Kind == CardKind.Confess && k.Ref == l.Id);
            Check("로맨스 — 고백: 찾아가 마음을 전했다 (대화 카드)", conf, kc != null ? $"{kc.Pick?.Text} ({kc.Pick?.Chance * 100:0}%) — {kc.Outcome}" : $"단계 {l.Stage}");
            if (l.Stage != LoveStage.Lovers) { Console.WriteLine($"     (닿지 않았다 — {kc?.Branch}) 다시 한 번"); st.Hold(CardKind.Confess, a, b, l.Id, roll: 0.01f); }
            Check("로맨스 — 연인이 됐다 (드러내거나 몰래 만나기로 이어진다)", l.Together && (l.Public || l.SchemeId >= 0), $"{StorySystem.LoveName(l.Stage)} · 드러냄 {l.Public} · 몰래 {l.Secret}");
            if (!l.Public) { var sc = w.Schemes.Get(l.SchemeId); if (sc != null) { sc.FoundAt = w.Tick; sc.Finder = third.Id; } l.NextCheck = 0; Run(w, SimTime.Minutes(11)); }
            Check("로맨스 — 주변 반응 (축하 · 수군거림)", st.Stats.Cheers + st.Stats.Whispers > 0, $"축하 {st.Stats.Cheers} · 수군 {st.Stats.Whispers} · 안건 {st.Stats.Agendas}");
            // 질투: 연인이 다른 사람과 가까운 걸 같은 방에서 본다
            Bond(third, a, 0.5f);
            l.Since = w.Tick - SimTime.Hours(30); l.Stage = LoveStage.Lovers;
            var room = w.Ship.Rooms.Where(r => r.Kind is RoomType.Lounge or RoomType.Mess).OrderBy(r => r.Id).FirstOrDefault() ?? a.Room!;
            var spot = room.Center;
            foreach (var (c, dx) in new[] { (a, -1f), (b, 0f), (third, 1f) }) { c.Position = spot + new Vector2(dx, 0f); c.PreviousPosition = c.Position; c.Path = null; }
            Run(w, 2);
            foreach (var (c, dx) in new[] { (a, -1f), (b, 0f), (third, 1f) }) { c.Position = spot + new Vector2(dx, 0f); }
            Run(w, 1);
            st.CheckLove(l);
            Check("로맨스 — 위기: 질투 (같은 방에서 본다)", l.Stage == LoveStage.Crisis && l.Rival == third.Id && st.Stats.Jealousy > 0, $"{l.CrisisWhy}");
            bool mend = TaleUntil(w, () => st.Cards.Any(k => k.Kind == CardKind.Mend && k.Ref == l.Id), SimTime.TicksPerDay, 60);
            var km = st.Cards.LastOrDefault(k => k.Kind == CardKind.Mend && k.Ref == l.Id);
            Check("로맨스 — 고비 카드: 넘기거나 갈라선다", mend && km != null, km != null ? $"{km.Pick?.Text} — {km.Outcome} · {km.Branch} · {StorySystem.LoveName(l.Stage)}" : "");
            // 끝 ①: 고비를 넘긴 연인이 청혼하고 배 안에서 식을 올린다 (v18.14 결혼식이 짝을 맺는다)
            if (l.Stage != LoveStage.Lovers) { l.Stage = LoveStage.Lovers; l.Rival = -1; }
            l.Crises = Math.Max(1, l.Crises); l.Since = w.Tick - SimTime.Hours(31); l.NextCheck = 0; Bond(a, b, 0.75f);
            int wed0 = st.Stats.Weddings;
            bool wed = TaleUntil(w, () => l.Stage == LoveStage.Married, SimTime.TicksPerDay * 3, 300);
            Check("로맨스 — 결혼: 청혼 → 식 → 부부 (짝이 맺어졌다)", wed && a.Partner == b.Id && b.Partner == a.Id && st.Stats.Weddings > wed0,
                $"{StorySystem.LoveName(l.Stage)} · 청혼 {(l.SchemeId == -2 ? "했다" : "안 했다")} · {string.Join(" / ", l.Beats.TakeLast(2).Select(x => x.text))}");
            // 끝 ②: 다른 연인 — 두 번째 고비에서 말이 엇나가면 헤어지고, 둘 다 '끝난 사랑'을 안고 산다 · 친구들은 편을 든다
            var c2 = p[3]; var d2 = p[4];
            Bond(c2, d2, 0.4f);
            var l2 = st.Crush(c2, d2); l2.Stage = LoveStage.Crisis; l2.Fails = 1; l2.Public = true;
            foreach (var f in p.Skip(5).Take(3)) { f.Affinity[c2.Id] = 0.7f; f.Affinity[d2.Id] = 0.1f; }
            int wh0 = st.Stats.Whispers;
            var kb = st.Hold(CardKind.Mend, c2, d2, l2.Id, roll: 0.999f);
            bool hb = new[] { c2, d2 }.Any(x => st.ArcOf(x)?.Spec.Key == "heartbreak");
            Check("로맨스 — 이별: 고비에서 갈라서고 '끝난 사랑'이 새 이야기로 · 친구들이 편을 든다", l2.Stage == LoveStage.Broken && hb && st.Stats.Whispers > wh0,
                $"{kb.Pick?.Text} — {kb.Outcome} · {kb.Branch} · 새 이야기 {hb} · 편 듦 {st.Stats.Whispers - wh0}");
        }

        // ── 6) 다른 카드: 공황 진정 · 다툼 중재 · 범인 추궁
        if (Do("6"))
        {
            var w = DayOne(seed, "Hanbit");
            var st = w.Tales; st.NoSeeds = true;
            var p = w.Crew.Where(c => !c.Dead && !c.IsChild).OrderBy(c => c.Id).ToList();
            var x = p.First(c => c.Room != null && c.IsAwake && p.Any(o => o != c && o.Room == c.Room && o.IsAwake)) ;
            x.Mind.PanicUntil = w.Tick + SimTime.Minutes(6);
            Run(w, SimTime.Minutes(2));
            var kp = st.Cards.LastOrDefault(k => k.Kind == CardKind.Calm && k.Listener == x.Id);
            Check("공황 — 곁에 있던 사람이 진정시키러 나섰다 (카드)", kp != null, kp != null ? $"{kp.Pick?.Text} ({kp.Pick?.Chance * 100:0}%) — {kp.Outcome} {kp.Branch}" : "");
            var victim = p[0]; var innocent = p[1];
            var pr = w.Schemes.Start(SchemeTable.Get("salt_sugar")!, p[2]);
            var ka = st.Hold(CardKind.Accuse, victim, innocent, pr.Id, roll: 0.99f);
            Check("추궁 — 억울한 사람을 몰면 누명이 새 갈래로", !ka.Success && ka.Branch.Contains("누명"), $"{ka.Pick?.Text} — {ka.Outcome} · {ka.Branch}");
            var ka2 = st.Hold(CardKind.Accuse, victim, p[2], pr.Id, roll: 0.01f);
            Check("추궁 — 범인을 맞게 짚으면 털어놓는다", ka2.Success && pr.Identified, ka2.Outcome);
            Bond(p[3], p[4], -0.6f); Bond(p[5], p[3], 0.5f); Bond(p[5], p[4], 0.5f);
            bool med = TaleUntil(w, () => st.Cards.Any(k => k.Kind == CardKind.Mediate), SimTime.Hours(14), 100);
            var km = st.Cards.LastOrDefault(k => k.Kind == CardKind.Mediate);
            Check("중재 — 둘 다와 가까운 사람이 찾아가 풀어 본다", med, km != null ? $"{w.Crew.First(c => c.Id == km.Speaker).Name}: {km.Pick?.Text} — {km.Outcome} {km.Branch}" : "");
            var cardText = st.Cards.SelectMany(k => k.Options.SelectMany(o => new[] { o.Text, o.Say, o.Lock }).Concat(new[] { k.Title, k.Scene, k.Outcome, k.Branch, k.Answer })).ToList();
            var leak = cardText.Where(t => TaleBad.Any(b => t.Contains(b))).ToList();
            Check("카드 — 화면 글에 개발 말투 · '선장'이 없다", leak.Count == 0, string.Join(" / ", leak.Take(3)));
        }

        // ── 7) 결정론 · 성능
        if (Do("7"))
        {
            uint H() { var w = World.CreateDefault(seed, 0, "Hanbit"); Run(w, SimTime.TicksPerDay + SimTime.Hours(6)); return SaveGame.StateHash(w); }
            uint h1 = H(), h2 = H();
            Check("결정론 (같은 시드 · 같은 지문)", h1 == h2, $"{h1:x8} / {h2:x8}");
            {
                var w = World.CreateDefault(seed, 0, "Hanbit");
                Run(w, SimTime.TicksPerDay * 3);
                var st = w.Tales;
                Console.WriteLine($"   사흘: {st.Stats.Line()}");
                Check("저절로 — 사흘 동안 이야기 · 잡담 · 밤 모임이 생긴다", st.Stats.Arcs >= 3 && st.Stats.Steps > st.Stats.Arcs && st.Stats.SmallTalks > 0 && st.Stats.Camps >= 2, "");
            }
            if (Environment.GetEnvironmentVariable("SHIPSIM_NOPERF") != "1")
            {
                double Day(bool off)
                {
                    StorySystem.Off = off;
                    var w = World.CreateDefault(seed, 0, "Cheonma");
                    Run(w, SimTime.Hours(2));
                    StorySystem.UpdateTicks = 0;
                    var sw = Stopwatch.StartNew();
                    Run(w, SimTime.TicksPerDay);
                    StorySystem.Off = false;
                    return sw.Elapsed.TotalSeconds;
                }
                double t0 = Math.Min(Day(true), Day(true)), t1 = Math.Min(Day(false), Day(false));
                double own = StorySystem.UpdateTicks * 1.0 / Stopwatch.Frequency;
                Console.WriteLine($"   성능 (30인 하루): 끔 {t0:0.0}초 · 켬 {t1:0.0}초 ({(t1 / t0 - 1) * 100:+0;-0;0}%) · 이 시스템 틱 {own * 1000:0}ms");
                Check("성능 · 30인 배 하루 ±10%", t1 < t0 * 1.10, $"{t0:0.0} → {t1:0.0}초");
            }
        }
        return _fails == 0 ? 0 : 1;
    }
}
