using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>
/// 내 사람 (docs/OBSERVER.md §6.1 · 6.2 · §16 N1 ~ N3 · M5 — 묶음 '내 사람'): 출항 소개 (띄우면 멈춘 채 [사람들]이 먼저 — 얼굴 · 이름 · 맡은 일 · 사람됨 · 꿈 · 지난 일 · 남몰래는 '관객만 안다' · ☆ ·
/// [이대로 출항] [항로 ›] [다른 사람들로]) · ☆ 눈여겨보기 (별 단 이의 일 → 알림 4초 · 누르면 그 이 · 별 없는 이는 없다) · 지나온 길 (사람 일 장부 — 맨 위 달라진 것) ·
/// ☆는 카메라만 (쌍둥이 — 하이라이트 · 장 · 세계 같음 · 조용할 때 그 방을 더 본다) · 자리표 (소개 판 · 일곱째 작은 탭 한 줄 · ☆ 단추).
/// </summary>
public static partial class Program
{
    private static void WatchRules()
    {
        if (!Do("내 사람")) return;
        Console.WriteLine("\n[내 사람 — 출항 소개 · ☆ 눈여겨보기 · 지나온 길]");
        if (Environment.GetEnvironmentVariable("WATCH_BENCH") == "1")
        {
            // 재기 (판정하지 않는다): 새뜰호 · 별 다섯 · 600틱 — 틱마다 ☆ 사실 보기 (새 사실만)
            var lay = Layout.Ship("Saetteul", Tables.Default);
            var w = ShipWorld.Crewed("Saetteul", 7, lay.Furniture.Count(f => f.Type == "Bed"), lay.Furniture.Count(f => f.Type == "RobotDock"), 0);
            var b = new LiveShow(w, lay.Name);
            foreach (var a in w.Get<Mind>().Agents.Where(a => a.IsPerson).Take(5)) b.ToggleStar(a.Id);
            var call = typeof(LiveShow).GetMethod("WatchStars", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.CreateDelegate<Action>(b);
            call();   // 데우기
            for (int i = 0; i < 600; i++) { w.Step(); b.Dir.Observe(w); call(); }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 100_000; i++) call();   // 새 사실이 없는 틱 (거의 모든 틱)
            double idle = sw.Elapsed.TotalMilliseconds * 1000 / 100_000;
            var people = w.Get<Mind>().Agents.Where(a => a.IsPerson).Select(a => a.Id).ToList();
            sw.Restart();
            for (int i = 0; i < 2000; i++)
            {
                for (int k = 0; k < 8; k++) w.Facts.Emit(w.Tick, FactKind.Talk, -1, (int)TalkEnd.Agree, 0, people[k % people.Count], people[(k + 1) % people.Count]);   // 별 단 이와 아닌 이의 여덟 사실 (알림은 없는 갈래)
                call();
            }
            double busy = sw.Elapsed.TotalMilliseconds * 1000 / 2000;
            Console.WriteLine($"    (☆ 재기) 새뜰호 별 다섯: 새 사실 없는 틱 {idle * 1000:0}ns · 새 사실 여덟 틱 {busy:0.00}µs");
        }

        // N1: 출항 소개 — 띄운 배 (출발 점화 전)를 붙잡으면 [사람들]이 먼저 열린다 · 사람마다 한 줄 · 남몰래 좋아함은 '관객만 안다' · ☆ · 멈춘 채 · 아래 [이대로 출항] [항로 ›] [다른 사람들로]
        {
            var show = new LiveShow(HandShip(101), "디딤호");
            var s = show.Sim; var m = s.Get<Mind>(); var so = s.Get<Society>();
            var people = m.Agents.Where(a => a.IsPerson).Select(a => a.Id).ToList();
            if (!so.Past.Any(t => t.Kind == 4)) so.Past.Add(new PastTie { A = people[0], B = people[1], Kind = 4 });   // 남몰래 좋아함이 하나는 있게
            if (!so.Past.Any(t => t.Kind == 3)) so.Past.Add(new PastTie { A = people[2], B = people[3], Kind = 3 });   // 묵은 앙금도
            var crush = so.Past.First(t => t.Kind == 4); var grudge = so.Past.First(t => t.Kind == 3);
            show.HoldForDeparture();
            long t0 = s.Tick;
            show.Advance(1.0);
            var f = show.Render();
            var rows = f.PanelRows.Where(r => r.Key >= LiveShow.KeyPerson(0) && r.Key < LiveShow.KeyPerson(0) + (1L << 32)).ToList();
            PanelRow RowOf(int id) => rows.FirstOrDefault(r => r.Key == LiveShow.KeyPerson(id));
            bool held = f.Holding && f.Paused && s.Tick == t0 && f.Panel == PanelKind.People && f.Detail == DetailKind.None && f.PanelTitle.Contains("함께 떠날");
            bool each = rows.Count == people.Count && people.All(id =>
            {
                var r = RowOf(id); var a = m[id];
                string role = a.C.Role < m.RoleNames.Length ? m.RoleNames[a.C.Role] : "";
                return r.Main != null && r.Face >= 0 && r.Main.StartsWith(m.NameOf(id)) && r.Main.Contains(role) && r.Sub.Contains(LiveShow.Persona(m, so, a).Split(" · ")[0]) && r.Act.Contains("눈여겨") && r.ActOn;
            });
            string cn = m.NameOf(crush.A), cb = m.NameOf(crush.B), ga = m.NameOf(grudge.A), gb = m.NameOf(grudge.B);
            bool past = RowOf(crush.A).Sub.Contains($"{cn} → {cb} — 남몰래 좋아함 (관객만 안다)") && RowOf(grudge.A).Sub.Contains($"{ga} ↔ {gb} — 묵은 앙금") && RowOf(grudge.B).Sub.Contains($"{ga} ↔ {gb} — 묵은 앙금")
                && !RowOf(crush.B).Sub.Contains($"{cn} → {cb}");
            // ☆ (소개 판의 줄 단추) → 별 · 단추 글이 바뀐다 · 한 번 더 누르면 끈다
            show.PanelAct(LiveShow.KeyPerson(people[0]));
            var f1 = show.Render();
            bool star = show.Stars.Contains(people[0]) && f1.Stars.Contains(people[0]) && f1.PanelRows.First(r => r.Key == LiveShow.KeyPerson(people[0])).Act.Contains("★") && show.Dir.Starred.Contains(people[0]);
            show.PanelAct(LiveShow.KeyPerson(people[0]));
            bool unstar = show.Stars.Count == 0 && show.Dir.Starred.Length == 0;
            // 자리: 소개 판 아래 [이대로 출항] [항로 ›] [다른 사람들로] (판 안 · 겹치지 않는다) · 가운데 출항 띠는 없다 (판에 있다)
            var hud = new Hud();
            HudLayout.Build(f, new HudState(), 1920, 1080, Hud.Approx, Hud.ApproxLines, hud);
            var hold = hud.Area("출항 줄"); var box = hud.Area("판");
            var hb = hud.Hits.Where(x => x.Act is UiAct.Depart or UiAct.RouteTab or UiAct.Recrew).ToList();
            bool place = !hold.Empty && InsideR(hold, box) && hb.Count == 3 && hb.All(x => InsideR(x.R, hold)) && hud.Area("출항 띠").Empty
                && !hb.Any(x => hb.Any(y => !ReferenceEquals(x, y) && x.R.Intersects(y.R)));
            // [항로 ›] → 항로 판 (출항 줄 그대로) · 출항하면 소개가 아닌 사람 판
            show.TogglePanel(PanelKind.Route);
            bool route = show.Panel == PanelKind.Route && show.Holding;
            show.Depart(); show.TogglePanel(PanelKind.People);
            bool after = !show.Holding && !show.PanelRows().Any(r => r.Act.Contains("눈여겨")) && !show.Render().PanelTitle.Contains("함께 떠날");
            Check("내 사람 N1: 출항 소개 — 붙잡으면 [사람들]이 먼저 (멈춘 채 · '함께 떠날 사람들') · 사람마다 얼굴 · 이름 · 맡은 일 · 사람됨 · 꿈 · 지난 일 (두 사람을 잇는 꼴 '가 ↔ 나 — 묵은 앙금' · 남몰래는 '가 → 나 — 남몰래 좋아함 (관객만 안다)' — 받는 쪽 줄에는 없다) · ☆ (누르면 ★ · 연출에 넘긴다 · 다시 누르면 끈다) · 판 아래 [이대로 출항] [항로 ›] [다른 사람들로] · 떠나면 보통 사람 판",
                held && each && past && star && unstar && place && route && after,
                $"붙잡음 {held} (틱 {t0}→{s.Tick} · '{f.PanelTitle}') · 줄 {rows.Count}/{people.Count} {each} · 지난 일 {past} ('{RowOf(crush.A).Sub?.Replace('\n', '/')}') · ☆ {star}/{unstar} · 자리 {place} (줄 {hold} · 단추 {hb.Count}) · 항로 {route} · 떠난 뒤 {after}");
        }

        // N2: ☆ 단 이에게 일이 나면 알림 4초 '☆ 김하늘이 쓰러졌다' (누르면 그 이를 따라간다) — 쓰러짐 · 다침 · 숨짐 · 다툼 (따짐 · 몸싸움 · 탓) · 편지 · 짝 · 꿈 / 별 없는 이는 없다 ·
        //     지나온 길 (자세히 일곱째 작은 탭 — 사람 일 장부): 맨 위 '겁이 많았다 → 맞서며 단단해졌다' · 구함 · 다침 · 짝 · 꿈을 때 차례로
        {
            var show = new LiveShow(HandShip(102), "디딤호") { FrameBudget = 1000 };
            show.Advance(0.05);
            var s = show.Sim; var m = s.Get<Mind>();
            var people = m.Agents.Where(a => a.IsPerson).Select(a => a.Id).ToList();
            int a0 = people[0], b0 = people[1], c0 = people[2];
            show.ToggleStar(a0);
            string Seen(Action emit)
            {
                show.TickNotice(60);
                emit();
                long t0 = s.Tick;
                for (int k = 0; k < 40 && s.Tick == t0; k++) show.Advance(0.05);   // 한 틱은 나아가게 (따라가는 동안은 느리다)
                return show.Render().Notice;
            }
            int at = m[a0].C.Cell;
            string na = m.NameOf(a0), nb = m.NameOf(b0);
            var said = new List<string> { Seen(() => s.Facts.Emit(s.Tick, FactKind.Collapsed, at, 0, 0, m[a0].C.Body)) };
            show.NoticeGo();   // 누르면 그 이를 따라간다
            bool go = show.Selected == a0 && show.FollowSelected;
            said.AddRange(new[]
            {
                Seen(() => s.Facts.Emit(s.Tick, FactKind.Hurt, at, 5, 0, m[a0].C.Body)),
                Seen(() => s.Facts.Emit(s.Tick, FactKind.Talk, at, (int)TalkEnd.Argue, 0, b0, a0)),
                Seen(() => s.Facts.Emit(s.Tick, FactKind.Fight, at, 1, 0, a0, b0)),
                Seen(() => s.Facts.Emit(s.Tick, FactKind.Blamed, at, 0, 0, b0, a0)),
                Seen(() => s.Facts.Emit(s.Tick, FactKind.Letter, at, 1, 0, a0)),
                Seen(() => s.Facts.Emit(s.Tick, FactKind.Relationship, at, 1, 0, a0, b0)),
                Seen(() => s.Facts.Emit(s.Tick, FactKind.Aim, at, 1, 0, a0, 0)),
            });
            string[] want = { "쓰러졌다", "다쳤다", "다퉜다", "몸싸움", "탓", "편지", "짝이 되었다", "꿈을 이루었다" };
            bool notes = said.Zip(want).All(p => p.First.StartsWith("☆ ") && p.First.Contains(p.Second) && p.First.Contains(na));
            string other = Seen(() => s.Facts.Emit(s.Tick, FactKind.Collapsed, m[c0].C.Cell, 0, 0, m[c0].C.Body));
            bool none = !other.StartsWith("☆");
            // 같은 일은 10분 안에 거듭 알리지 않는다 (다침이 틱마다 나도)
            string again = Seen(() => s.Facts.Emit(s.Tick, FactKind.Hurt, at, 5, 0, m[a0].C.Body));
            bool calm = !again.StartsWith("☆");
            // 지나온 길: 겁 → 단단해짐 · 구함 · 짝 · 꿈 (때 차례)
            Seen(() => s.Facts.Emit(s.Tick, FactKind.Heart, at, (int)HeartEvent.Fearful, 0, a0));
            Seen(() => s.Facts.Emit(s.Tick, FactKind.Rescue, at, 0, 0, a0, c0));
            Seen(() => s.Facts.Emit(s.Tick, FactKind.Heart, at, (int)HeartEvent.Hardened, 0, a0));
            show.OpenDetail(DetailKind.Person, a0); show.SetDetailSub(6);
            var path = show.PanelRows();
            int Find(string w) => Array.FindIndex(path, r => (r.Main + " " + r.Sub).Contains(w));
            bool road = path.Length > 0 && path[0].Main.Contains("겁이 많았다 → 맞서며 단단해졌다") && Find("쓰러졌다") > 0 && Find("짝") > Find("쓰러졌다") && Find("꿈") > Find("짝")
                && Find("구했다") > Find("꿈") && Find(m.NameOf(c0)) >= 0 && show.Render().DetailTabs.Length == 7 && show.Render().DetailTabs[6] == "지나온 길";
            Check("내 사람 N2: ☆ 단 이의 일 → 알림 '☆ 이름…' (쓰러짐 · 다침 · 다툼 — 따짐 · 몸싸움 · 탓 · 편지 · 짝 · 꿈) · 누르면 그 이를 따라간다 · 별 없는 이는 없다 · 같은 일 10분 안 한 번 / 지나온 길 — 맨 위 '겁이 많았다 → 맞서며 단단해졌다' · 쓰러짐 · 짝 · 꿈 · 구함을 때 차례로",
                notes && none && go && calm && road,
                $"알림 {notes} [{string.Join(" | ", said)}] · 별 없는 이 '{other}' · 누름 {go} · 거듭 '{again}' · 지나온 길 {road} [{string.Join(" | ", path.Take(6).Select(r => r.Main))}]");
        }

        // N3: ☆는 카메라만 — ☆ 있음 · 없음 쌍둥이 (불씨 낀 1.5분): 세계 바이트 · 하이라이트 · 오늘 장 같음 / 조용할 때 (일상 비춤) 별 단 이의 방을 먼저 · 더 오래 (15초 → 25초)
        {
            var a = new LiveShow(HandShip(103), "디딤호"); var b = new LiveShow(HandShip(103), "디딤호");
            var m = a.Sim.Get<Mind>();
            int pick = m.Agents.Where(x => x.IsPerson).Select(x => x.Id).Last();
            a.ToggleStar(pick);
            foreach (var x in new[] { a, b }) { var aim = HandTargets(x.Sim); x.FastForward(20); x.Sim.Get<Hand>().Enqueue(Order(HandKind.Fire, aim.Bed, 2)); x.FastForward(Time.Minutes(1.5)); }
            bool same = a.Sim.Save().AsSpan().SequenceEqual(b.Sim.Save()) && HighlightsOf(a.Dir) == HighlightsOf(b.Dir) && a.Dir.ChapterNow(a.Sim).Title == b.Dir.ChapterNow(b.Sim).Title && a.Dir.Highlights.Count > 0;
            // 조용할 때: 세계는 그대로 둔 채 연출만 1,200번 본다 (화면 시간이 흐른다 — 일상 비춤이 방을 돈다) · 별 단 이의 방을 비춘 번 수
            var c = new LiveShow(HandShip(104), "디딤호"); var d = new LiveShow(HandShip(104), "디딤호");
            var mc = c.Sim.Get<Mind>(); var L = c.Sim.Layout;
            c.FastForward(3); d.FastForward(3);
            // 별: 사람이 가장 적은 방의 사람 (처음엔 사람 많은 방부터 비춘다 — 별이 없으면 늦게 온다)
            var byRoom = mc.Agents.Where(x => x.IsPerson && x.C.Cell >= 0 && L.Room[x.C.Cell] >= 0).GroupBy(x => L.Room[x.C.Cell]).OrderBy(g => g.Count()).ThenByDescending(g => g.Key).ToList();
            int star = byRoom[0].First().Id, room = L.Room[mc[star].C.Cell];
            c.ToggleStar(star);
            int Count(LiveShow x, out int first)
            {
                int n = 0; first = -1;
                for (int i = 0; i < 1200; i++)
                {
                    x.Dir.Observe(x.Sim);
                    bool on = x.Dir.FocusCell >= 0 && L.Room[x.Dir.FocusCell] == room;
                    if (on) { n++; if (first < 0) first = i; }
                }
                return n;
            }
            int withStar = Count(c, out int firstWith), without = Count(d, out int firstWithout);
            bool camera = withStar > without && withStar > 0 && (firstWithout < 0 || firstWith <= firstWithout) && c.Dir.Highlights.Count == d.Dir.Highlights.Count && c.Sim.Tick == d.Sim.Tick;
            Check("내 사람 N3: ☆는 카메라만 — 별 있음 · 없음 쌍둥이 (불씨 낀 1.5분) 세계 바이트 · 하이라이트 · 오늘 장 같음 / 조용할 때 일상 비춤은 별 단 이의 방을 먼저 · 더 오래 (연출 1,200번 — 그 방을 비춘 번 수가 늘었다)",
                same && camera,
                $"같음 {same} (하이라이트 {a.Dir.Highlights.Count}) · 별 방 비춘 번 {withStar} (처음 {firstWith}) / 별 없이 {without} (처음 {firstWithout}) · 방 {room}");
        }

        // M5: 자리표 — 소개 판 (사람 3 · 12 · 40 · 100 — 얼굴 · 줄 단추 · 출항 줄) · 일곱째 작은 탭 (지나온 길 — 1280×720에서도 한 줄) · ☆ 단추 (사람 카드 · 자세히 머리) — 여섯 창 × 글 100 · 150%: 겹침 0 · 밖 0 · 누를 자리 크기 · 글자
        {
            var hud = new Hud(); var bad = new List<string>(); int layouts = 0;
            foreach (var (pw, ph) in new[] { (1920, 1080), (1920, 1010), (1600, 900), (1366, 768), (1280, 720), (2560, 1440) })
                foreach (float scale in new[] { 1f, 1.5f })
                    foreach (int n in new[] { 3, 12, 40, 100 })
                    {
                        string tag = $"{pw}×{ph} {scale * 100:0}% 사람 {n}";
                        Hud.Measure ms = (t, px, b) => Hud.Approx(t, px, b) * scale; Hud.MeasureLines ml = (t, px, w, k) => Hud.ApproxLines(t, (int)(px * scale), w, k);
                        // 소개 판
                        var f = FakeFrame(n, true);
                        f.Holding = true; f.Paused = true; f.Panel = PanelKind.People; f.PanelTitle = "함께 떠날 사람들";
                        f.PanelRows = Enumerable.Range(0, n).Select(i => new PanelRow($"김하늘{i} · 기관사", "말이 많고 용감하다 · 꿈: 귀향 · 즐겨 하는 것: 음악 듣기\n지난 일: 김하늘 ↔ 이도윤 — 묵은 앙금 · 김하늘 → 정지우 — 남몰래 좋아함 (관객만 안다)", false, i, Key: LiveShow.KeyPerson(i), Act: i % 2 == 0 ? "☆ 눈여겨보기" : "★ 눈여겨봄")).ToArray();
                        f.PanelPick = 0; f.PanelPickKey = f.PanelRows[0].Key; f.Stars = new[] { 1 };
                        HudLayout.Build(f, new HudState(), pw, ph, ms, ml, hud); layouts++;
                        var hold = hud.Area("출항 줄"); var box = hud.Area("판");
                        var hb = hud.Hits.Where(x => x.Act is UiAct.Depart or UiAct.RouteTab or UiAct.Recrew).ToList();
                        if (hold.Empty || !InsideR(hold, box) || hb.Count != 3 || hb.Any(x => !InsideR(x.R, hold) || x.R.H < 32) || !hud.Area("출항 띠").Empty) bad.Add($"{tag} 소개: 줄 {hold} · 단추 {hb.Count}");
                        foreach (var x in hud.Hits.Where(x => x.Act == UiAct.PanelRowAct)) if (x.R.W < 90 || x.R.H < 28 || !InsideR(x.R, box)) bad.Add($"{tag} 소개 ☆ {x.R}");
                        var star = hud.Hits.FirstOrDefault(x => x.Act == UiAct.Star);   // 사람 카드의 ☆ (고른 이 0)
                        if (star == null || !InsideR(star.R, hud.Area("카드")) || star.R.W < 28 || star.R.H < 28) bad.Add($"{tag} 카드 ☆ {star?.R}");
                        foreach (var o in hud.Overlaps()) bad.Add($"{tag} 소개: {o}");
                        foreach (var o in hud.Outside()) bad.Add($"{tag} 소개: 밖 {o}");
                        // 사람 자세히 — 작은 탭 일곱 (한 줄 · 판 안) · 머리의 ☆
                        var fd = FakeFrame(n, true, true, PanelKind.People, DetailKind.Person);
                        fd.DetailTabs = LiveShow.PersonTabs; fd.DetailSub = 6; fd.DetailId = 0; fd.Stars = new[] { 0 };
                        HudLayout.Build(fd, new HudState(), pw, ph, ms, ml, hud); layouts++;
                        var tabs = hud.Hits.Where(x => x.Act == UiAct.DetailTab).ToList(); var pb = hud.Area("판");
                        if (tabs.Count != 7 || tabs.Select(x => x.R.Y).Distinct().Count() != 1 || tabs.Any(x => !InsideR(x.R, pb) || x.R.W < 44)) bad.Add($"{tag} 작은 탭 {tabs.Count} · 줄 {tabs.Select(x => x.R.Y).Distinct().Count()} · 끝 {(tabs.Count > 0 ? tabs[^1].R.Right : 0):0}/{pb.Right:0}");
                        var hs = hud.Hits.FirstOrDefault(x => x.Act == UiAct.Star && x.Key == 0 && InsideR(x.R, hud.Area("자세히 머리")));
                        if (hs == null || !hs.On || hs.R.H < 28) bad.Add($"{tag} 머리 ☆ {hs?.R}");
                        foreach (var o in hud.Overlaps()) bad.Add($"{tag} 자세히: {o}");
                        foreach (var o in hud.Outside()) bad.Add($"{tag} 자세히: 밖 {o}");
                    }
            var texts = new[] { LiveShow.IntroTitle, LiveShow.StarOn, LiveShow.StarOff, HudLayout.StarTip(true), HudLayout.StarTip(false) };   // 바닥 글의 키 이름 (P)은 다른 판과 같다
            var glyph = texts.Where(t => t.Any(ch => Hud.Missing.Contains(ch)) || System.Text.RegularExpressions.Regex.IsMatch(t, "[A-Za-z{}_]")).Concat(LiveShow.IntroFoot.Any(ch => Hud.Missing.Contains(ch)) ? new[] { LiveShow.IntroFoot } : Array.Empty<string>()).ToList();
            Check("내 사람 M5: 소개 판 (사람 3 · 12 · 40 · 100 — 줄 단추 ☆ · 판 아래 출항 줄 · 가운데 출항 띠 없음) · 사람 카드 ☆ · 자세히 작은 탭 일곱 (지나온 길 — 1280×720 · 150%도 한 줄) · 머리 ☆ — 여섯 창 × 글 100 · 150%: 겹침 0 · 밖 0 · 누를 자리 크기 · 글자",
                bad.Count == 0 && glyph.Count == 0, $"자리표 {layouts}가지 · 어긋남 {bad.Count} {string.Join(" | ", bad.Take(3))} · 글자 {string.Join(" ", glyph)}");
        }
    }
}
