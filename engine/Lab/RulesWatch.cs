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

    /// <summary>
    /// 놓치지 않게 (docs/OBSERVER.md §6.3 ~ 6.6 · §16 N4 ~ N7 · Z2 · M5 — 묶음 '놓치지 않게'): 큰 일 멈춤 (죽음 · 쓰러짐 · 외판 구멍 켬 — 그 사실이 난 틱에서 멈춘다 · 같은 뿌리 10분 안 한 번 · 불 · ☆의 일은 끔 ·
    /// 감기 · 다시보기는 멈추지 않는다) · 창이 뒤로 가면 (멈춤 · 느리게 · 그대로 — 돌아오면 되돌린다) · '그사이' 카드 (손 없이 2분 + 게임 1시간 또는 하이라이트) · 길잡이 (함장마다 한 번 — 설정에 남는다) ·
    /// 산 세계 사실만 (미리 보기 세계의 사실로는 멈추지 않는다) · 자리표 (멈춤 카드 · 그사이 카드 · 길잡이 · 메뉴 갈래 고르기).
    /// </summary>
    private static void MissRules()
    {
        if (!Do("놓치지 않게")) return;
        Console.WriteLine("\n[놓치지 않게 — 큰 일 멈춤 · 창이 뒤로 가면 · 그사이 카드 · 길잡이]");
        static string Hm(long t) { long mm = t % Time.TicksPerDay * 60 / Time.TicksPerHour; return $"{mm / 60:00}:{mm % 60:00}"; }
        if (Environment.GetEnvironmentVariable("STOP_BENCH") == "1")
        {
            // 재기 (판정하지 않는다): 새뜰호 · 600틱 — 틱마다 멈춤 사실 보기 (새 사실만 · 켠 갈래 다섯)
            var lay = Layout.Ship("Saetteul", Tables.Default);
            var w = ShipWorld.Crewed("Saetteul", 7, lay.Furniture.Count(f => f.Type == "Bed"), lay.Furniture.Count(f => f.Type == "RobotDock"), 0);
            var b = new LiveShow(w, lay.Name);
            foreach (StopKind k in Enum.GetValues<StopKind>()) b.Prefs.SetStop(k, true);
            var call = typeof(LiveShow).GetMethod("CheckStops", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.CreateDelegate<Func<Sim, bool>>(b);
            call(w);
            for (int i = 0; i < 600; i++) { w.Step(); b.Dir.Observe(w); call(w); }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 100_000; i++) call(w);   // 새 사실이 없는 틱 (거의 모든 틱)
            double idle = sw.Elapsed.TotalMilliseconds * 1000 / 100_000;
            var people = w.Get<Mind>().Agents.Where(a => a.IsPerson).Select(a => a.Id).ToList();
            sw.Restart();
            for (int i = 0; i < 2000; i++)
            {
                for (int k = 0; k < 8; k++) w.Facts.Emit(w.Tick, FactKind.Talk, -1, (int)TalkEnd.Agree, 0, people[k % people.Count], people[(k + 1) % people.Count]);   // 멈추지 않는 갈래의 여덟 사실
                call(w);
            }
            double busy = sw.Elapsed.TotalMilliseconds * 1000 / 2000;
            Console.WriteLine($"    (멈춤 재기) 새뜰호 갈래 다섯: 새 사실 없는 틱 {idle * 1000:0}ns · 새 사실 여덟 틱 {busy:0.00}µs");
        }

        // N4 (가): 산 세계의 외판 구멍 (함장의 손 — 월드 길) → 그 구멍이 난 틱에서 멈춤 · 카드 '멈춤 — 외판에 구멍이 났다 (방 · 때)' · 멈춘 동안 세계가 서 있다 · [계속] /
        //          다시보기 (그 구멍이 다시 나는 세계)는 멈추지 않는다
        {
            var show = new LiveShow(HandShip(111), "디딤호") { FrameBudget = 1000, Prefs = new ViewPrefs() };   // 함장의 기본 설정 (게임이 넘긴다 — 넘기지 않으면 사람 없는 띄우기라 멈추지 않는다)
            var s = show.Sim; var L = s.Layout; var aim = HandTargets(s);
            show.SetFixed(3);   // ×600 — 한 장 (0.2초)에 50틱
            show.FastForward(Time.Minutes(2) + 5);   // 다시보기 고리의 첫 찍기 (2분) 뒤에 손을 넣는다 — 다시보기가 구멍 앞에서 시작하게
            long t0 = s.Tick, f0 = s.Facts.Total;
            s.Get<Hand>().Enqueue(Order(HandKind.Hole, aim.Hull, 2));
            for (int i = 0; i < 10 && show.StopLine.Length == 0; i++) show.Advance(0.2);
            var br = s.Facts.Since(f0).Where(x => x.Kind == FactKind.WallBreach && x.Cell >= 0 && L.Hull[x.Cell]).Select(x => (Fact?)x).FirstOrDefault();
            string line = show.StopLine; long at = s.Tick;
            bool stop = br != null && at == br.Value.Tick + 1 && show.Paused && line.StartsWith("멈춤 — 외판에 구멍이 났다 (") && line.EndsWith($" · {Hm(br.Value.Tick)})") && show.Render().StopLine == line;
            show.Advance(0.5);
            bool still = s.Tick == at && show.StopLine == line;
            show.TogglePause();   // 스페이스 = [계속]
            bool go = !show.Paused && show.StopLine.Length == 0;
            show.Advance(0.2);
            long live = s.Tick;
            show.StartReplay(new Highlight { Tick = live, StartTick = t0, Last = live, Title = "외판 구멍" });
            var rw = show.World;   // 다시보기 세계 (끝나도 남는다)
            long seen0 = rw.Facts.CountOf(FactKind.WallBreach); int replayStops = 0; bool replayed = show.Replaying && rw != s;
            for (int i = 0; i < 200 && show.Replaying; i++)
            {
                show.Advance(0.2);
                if (show.StopLine.Length > 0) replayStops++;
            }
            bool sawBreach = rw.Facts.CountOf(FactKind.WallBreach) > seen0 && rw.Tick > t0;
            bool replay = replayed && sawBreach && replayStops == 0 && s.Tick == live;
            show.StopReplay();
            Check("놓치지 않게 N4 (가): 외판 구멍 (함장의 손) → 구멍이 난 틱에서 멈춤 · 카드 '멈춤 — 외판에 구멍이 났다 (방 · 때)' · 멈춘 동안 세계가 서 있다 · 스페이스로 계속 / 다시보기 (구멍이 다시 나는 세계)는 멈추지 않는다",
                stop && still && go && replay,
                $"멈춤 {stop} (구멍 틱 {br?.Tick} · 멈춘 틱 {at} · '{line}') · 서 있음 {still} · 계속 {go} · 다시보기 {replay} (돌았나 {replayed} · 구멍 {sawBreach} · 멈춤 {replayStops})");
        }

        // N4 (나): 쓰러짐 · 숨짐 · 갈래 · 같은 뿌리 10분 · 감기 — 사실을 곧바로 넣는다 (그 틱에서 멈춘다)
        {
            var show = new LiveShow(HandShip(112), "디딤호") { FrameBudget = 1000, Prefs = new ViewPrefs() };
            var s = show.Sim; var m = s.Get<Mind>(); var L = s.Layout;
            var people = m.Agents.Where(a => a.IsPerson).Select(a => a.Id).ToList();
            int a0 = people[0], a1 = people[1], a2 = people[2];
            show.SetFixed(3); show.Advance(0.01);   // ×600 — 한 장 (0.02초)에 5틱 · 빨리 감는 동안이라 미리 보기는 쉰다
            show.TickNotice(60);
            string Room(int c) => c >= 0 && L.Room[c] >= 0 ? L.Rooms[L.Room[c]].Name : "";
            (bool stopped, long ran, string line) Emit(FactKind k, int actor, int cause, int cell = -1, int other = -1, float size = 0)
            {
                int at = cell >= 0 ? cell : m[a0].C.Cell;
                long t0 = s.Tick;
                s.Facts.Emit(s.Tick, k, at, size, cause, actor, other);
                show.Advance(0.02);
                return (show.StopLine.Length > 0, s.Tick - t0, show.StopLine);
            }
            int Root(string why) => s.Causes.Add(CauseKind.Setup, why, s.Tick, m[a0].C.Cell);
            var bad = new List<string>();
            // 쓰러짐 → 그 틱에서 멈춘다 · 글 '멈춤 — 김하늘이 쓰러졌다 (식당 · 07:04)'
            int c1 = Root("시험 — 연기를 마셨다");
            long tc = s.Tick;
            var e1 = Emit(FactKind.Collapsed, m[a0].C.Body, c1, m[a0].C.Cell);
            string want1 = $"멈춤 — {Ko.I(m.NameOf(a0))} 쓰러졌다 ({Room(m[a0].C.Cell)} · {Hm(tc)})";
            if (!e1.stopped || e1.ran != 1 || e1.line != want1 || !show.Paused) bad.Add($"쓰러짐 '{e1.line}' ≠ '{want1}' · 돈 틱 {e1.ran}");
            // [보기] → 그 이를 따라간다 · 다시 간다
            show.StopSee();
            if (show.Selected != a0 || !show.FollowSelected || show.Paused || show.StopLine.Length > 0) bad.Add($"보기: 고른 이 {show.Selected} · 따라감 {show.FollowSelected} · 멈춤 {show.Paused}");
            // 같은 뿌리 (불 하나에 쓰러짐 셋) 10분 안 → 멈추지 않는다
            int c1b = s.Causes.Add(CauseKind.Biology, "숨이 멎었다", s.Tick, m[a1].C.Cell, c1);
            var e2 = Emit(FactKind.Collapsed, m[a1].C.Body, c1b, m[a1].C.Cell);
            if (e2.stopped || e2.ran < 2) bad.Add($"같은 뿌리 둘째 '{e2.line}' · 돈 틱 {e2.ran}");
            // 불 · ☆의 일은 기본 끔 · 안쪽 벽이 뚫린 것은 외판 구멍이 아니다
            var e3 = Emit(FactKind.Ignition, -1, Root("시험 — 불씨"), show.Sim.Layout.Furniture.First(x => x.Type == "Bed").Cells[0]);
            if (e3.stopped) bad.Add($"불 (끔) '{e3.line}'");
            int inner = Enumerable.Range(0, L.N).First(c => L.Kind[c] == CellKind.Wall && !L.Hull[c] && L.Room[c] < 0);
            var e4 = Emit(FactKind.WallBreach, -1, Root("시험 — 안쪽 벽"), inner, size: 0.1f);
            if (e4.stopped) bad.Add($"안쪽 벽 '{e4.line}'");
            show.ToggleStar(a2);
            var e5 = Emit(FactKind.Hurt, m[a2].C.Body, Root("시험 — 다침"), m[a2].C.Cell, size: 5);
            if (e5.stopped) bad.Add($"☆ (끔) '{e5.line}'");
            // 다른 뿌리의 숨짐 → 멈춘다 '숨졌다'
            long td = s.Tick;
            var e6 = Emit(FactKind.Died, m[a2].C.Body, Root("시험 — 숨짐"), m[a2].C.Cell);
            if (!e6.stopped || e6.ran != 1 || e6.line != $"멈춤 — {Ko.I(m.NameOf(a2))} 숨졌다 ({Room(m[a2].C.Cell)} · {Hm(td)})") bad.Add($"숨짐 '{e6.line}'");
            show.StopGo();
            // 감기 (FastForward)에서는 멈추지 않는다 — 감은 동안의 큰 일은 감은 뒤에도 멈추지 않는다
            s.Facts.Emit(s.Tick, FactKind.Died, m[a1].C.Cell, 0, Root("시험 — 감는 동안"), m[a1].C.Body);
            show.FastForward(3);
            show.Advance(0.02);
            if (show.StopLine.Length > 0 || show.Paused) bad.Add($"감기 '{show.StopLine}'");
            // 10분 뒤 같은 뿌리는 다시 멈춘다
            show.FastForward(Time.Minutes(10));
            var e7 = Emit(FactKind.Collapsed, m[a1].C.Body, c1b, m[a1].C.Cell);
            if (!e7.stopped) bad.Add("10분 뒤 같은 뿌리 — 멈추지 않았다");
            show.StopGo();
            // 갈래 바꾸기 (메뉴 [알림 멈춤 ›]): 불 켬 → 멈춘다 · 쓰러짐 끔 → 멈추지 않는다 · ☆ 켬 → 별 단 이가 다치면 멈춘다
            show.ToggleStop(StopKind.Fire); show.ToggleStop(StopKind.Collapsed); show.ToggleStop(StopKind.Star);
            var e8 = Emit(FactKind.Ignition, -1, Root("시험 — 둘째 불씨"), L.Furniture.First(x => x.Type == "Bed").Cells[0]);
            if (!e8.stopped || !e8.line.StartsWith("멈춤 — ") || !e8.line.Contains("불이 붙었다")) bad.Add($"불 (켬) '{e8.line}'");
            show.StopGo();
            var e9 = Emit(FactKind.Collapsed, m[a0].C.Body, Root("시험 — 쓰러짐 끔"));
            if (e9.stopped) bad.Add($"쓰러짐 (끔) '{e9.line}'");
            var e10 = Emit(FactKind.Hurt, m[a2].C.Body, Root("시험 — ☆ 다침"), m[a2].C.Cell, size: 5);
            if (!e10.stopped || !e10.line.StartsWith($"멈춤 — ☆ {Ko.I(m.NameOf(a2))} 다쳤다")) bad.Add($"☆ (켬) '{e10.line}'");
            show.SetFixed(3);   // 빠르기를 고르면 카드를 닫고 간다
            if (show.StopLine.Length > 0 || show.Paused) bad.Add("빠르기를 골라도 멈춤 카드가 남았다");
            var frame = show.Render();
            // 사람 없는 띄우기 (게임이 설정을 넘기지 않은 실험실 · 관찰 장면): [계속]을 누를 이가 없으니 멈추지 않는다
            var bare = new LiveShow(HandShip(112), "디딤호") { FrameBudget = 1000 };
            bare.SetFixed(3); bare.Advance(0.01);
            var bm = bare.Sim.Get<Mind>();
            bare.Sim.Facts.Emit(bare.Sim.Tick, FactKind.Died, bm[a0].C.Cell, 0, bare.Sim.Causes.Add(CauseKind.Setup, "시험 — 사람 없는 띄우기", bare.Sim.Tick), bm[a0].C.Body);
            bare.Advance(0.02);
            if (bare.StopLine.Length > 0 || bare.Paused) bad.Add($"사람 없는 띄우기가 멈췄다 '{bare.StopLine}'");
            bool prefs = !show.Prefs.Stop(StopKind.Collapsed) && show.Prefs.Stop(StopKind.Fire) && show.Prefs.Stop(StopKind.Star) && show.Prefs.Dirty && frame.StopKinds.SequenceEqual(new[] { true, false, true, true, true });
            if (!prefs) bad.Add("설정이 바뀌지 않았다");
            Check("놓치지 않게 N4 (나): 쓰러짐 → 그 틱에서 멈춤 · '멈춤 — 김하늘이 쓰러졌다 (식당 · 07:04)' · [보기] → 그 이를 따라간다 / 같은 뿌리 10분 안 둘째는 멈추지 않음 (10분 뒤엔 다시) · 불 · ☆의 일 · 안쪽 벽은 멈추지 않음 · 숨짐은 멈춤 / 감기 · 사람 없는 띄우기 (설정을 넘기지 않은 실험실)는 멈추지 않음 / 갈래를 바꾸면 그대로 (불 켬 · 쓰러짐 끔 · ☆ 켬) · 빠르기를 고르면 카드를 닫고 간다",
                bad.Count == 0, bad.Count == 0 ? $"'{want1}' · '{e6.line}' · '{e8.line}' · '{e10.line}'" : string.Join(" | ", bad));
        }

        // N5: 창이 뒤로 가면 — 멈춤 (기본) · 느리게 (하루 24분 — 고정 빠르기도) · 그대로 / 돌아오면 되돌린다 · 멈춰 둔 채 갔으면 멈춘 채
        {
            var show = new LiveShow(HandShip(113), "디딤호") { FrameBudget = 1000 };
            var s = show.Sim;
            show.SetSpeed(3);   // 하루 1분
            show.Advance(0.05);
            var bad = new List<string>();
            show.Background(true);
            long t = s.Tick; show.Advance(0.5);
            if (!show.Paused || !show.Away || s.Tick != t) bad.Add($"멈춤: 멈춤 {show.Paused} · 틱 {t}→{s.Tick}");
            show.Background(false);
            if (show.Paused || show.Away) bad.Add("멈춤: 돌아와도 멈춰 있다");
            show.TogglePause(); show.Background(true); show.Background(false);
            if (!show.Paused) bad.Add("멈춰 둔 채 갔는데 돌아오니 간다");
            show.TogglePause();
            show.SetAwayMode(AwayMode.Slow);
            show.Background(true);
            if (show.Paused || show.DayMinutes != 24 || show.SpeedOverride != 0) bad.Add($"느리게: 하루 {show.DayMinutes}분 · 고정 {show.SpeedOverride}");
            show.Background(false);
            if (show.DayMinutes != 1 || show.SpeedPick != 3) bad.Add($"느리게: 돌아와 하루 {show.DayMinutes}분 · 단추 {show.SpeedPick}");
            show.SetFixed(2); double so = show.SpeedOverride;
            show.Background(true);
            if (show.SpeedOverride != 0 || show.DayMinutes != 24) bad.Add($"느리게 (고정): 고정 {show.SpeedOverride} · 하루 {show.DayMinutes}분");
            show.Background(false);
            if (show.SpeedOverride != so || show.FixedPick != 2 || show.SpeedPick != -2) bad.Add($"느리게 (고정): 돌아와 고정 {show.SpeedOverride} · {show.FixedPick}");
            show.SetAwayMode(AwayMode.Keep);
            show.Background(true);
            if (show.Paused || show.SpeedOverride != so) bad.Add("그대로: 바뀌었다");
            show.Background(false);
            bool prefs = show.Prefs.AwayIs == AwayMode.Keep && show.Prefs.Dirty;
            if (!prefs) bad.Add("설정이 바뀌지 않았다");
            Check("놓치지 않게 N5: 창이 뒤로 가면 — 멈춤 (기본 — 세계가 선다) · 느리게 (하루 24분 · 고정 빠르기도) · 그대로 / 돌아오면 고른 빠르기로 되돌린다 · 멈춰 둔 채 갔으면 멈춘 채 · 설정에 남는다",
                bad.Count == 0, bad.Count == 0 ? "멈춤 · 느리게 · 그대로 · 되돌림" : string.Join(" | ", bad));
        }

        // N6: '그사이' 카드 — 손 없이 2분 + 게임 1시간 (또는 하이라이트) → 다음 손이 닿을 때 장 · 장면 · 숨진 이 · 바뀐 것 / 손이 있었으면 없다 · 바뀐 게 없으면 없다 · 창이 돌아올 때도
        {
            var show = new LiveShow(HandShip(114), "디딤호");
            show.SetFixed(4);   // 빨리 감는 빠르기 — 미리 보기를 쉰다 (시험을 짧게 — 카드는 산 세계 · 연출 장부만 읽는다)
            var s = show.Sim; var m = s.Get<Mind>(); var life = s.Get<Life>();
            var people = m.Agents.Where(a => a.IsPerson).Select(a => a.Id).ToList();
            void Idle(double sec) { bool p = show.Paused; show.Paused = true; show.Advance(sec); show.Paused = p; }
            void Scene(string title) => show.Dir.Highlights.Add(new Highlight { Tick = s.Tick, StartTick = s.Tick, Last = s.Tick, Title = title });
            show.FastForward(3);
            show.Touched(); Idle(1.5);   // 손이 끊겼다 — 그사이 표 한 번
            long from = s.Tick;
            int dead = people[0], at = m[dead].C.Cell;
            int k1 = s.Causes.Add(CauseKind.Setup, "침대 밑 불씨", s.Tick, at), k2 = s.Causes.Add(CauseKind.Fire, "연기가 찼다", s.Tick, at, k1), k3 = s.Causes.Add(CauseKind.Biology, "숨이 멎었다", s.Tick, at, k2);
            { ref var b = ref life[m[dead].C.Body]; b.Alive = 0; b.Conscious = 0; b.DiedAt = s.Tick; }
            s.Facts.Emit(s.Tick, FactKind.Died, at, 0, k3, m[dead].C.Body);
            long died = s.Tick;
            show.FastForward(2);   // 연출이 숨짐을 본다
            s.Run((int)Time.Hours(1) + 3);   // 한 시간 (세계만 — 그사이 카드는 산 세계 · 연출 장부만 읽는다)
            string title = $"1일째 — {Ko.Eul(m.NameOf(dead))} 잃은 날";
            show.Dir.Chapters.Add(new Chapter { Day = 0, From = from, To = s.Tick, Title = title });
            Scene("침실 불이 번졌다");
            int hl = show.Dir.Highlights.Count;
            Idle(121);
            bool none0 = show.AwayCard.Length == 0;   // 손이 닿기 전에는 없다
            show.Touched();
            var card = show.Render().AwayCard;   // 그림에 실린 줄 (한 번만 그린다)
            string All(string head) => card.FirstOrDefault(l => l.StartsWith(head)) ?? "";
            bool head = card.Length > 0 && card[0].StartsWith("그사이 — 1시간이 지났다 (") && card[0].Contains(" → ");
            bool chapter = All("장 · ").Contains(title);
            bool lost = All("숨진 이 · ").StartsWith($"숨진 이 · {m.NameOf(dead)} ({Hm(died)} · 연기가 찼다)");
            bool changed = All("바뀐 것 · ").Contains($"사람 {people.Count} → {people.Count - 1}");
            bool scenes = All("장면 ").StartsWith("장면 ▶") && All("장면 ").Contains("침실 불이 번졌다");
            show.AwayClose();
            bool closed = show.AwayCard.Length == 0;
            // 손이 있었다 (30초마다) → 하이라이트가 나도 없다
            show.Touched(); Idle(1.5);
            Scene("식당에서 다툼이 났다"); show.FastForward(5);
            for (int i = 0; i < 5; i++) { Idle(30); show.Touched(); }
            bool touched = show.AwayCard.Length == 0;
            // 바뀐 게 없다 (게임 2분 · 하이라이트 없음) → 없다
            show.Touched(); Idle(1.5);
            show.FastForward(Time.Minutes(2));
            Idle(121); show.Touched();
            bool quiet = show.AwayCard.Length == 0;
            // 창이 뒤로 갔다 돌아올 때도 (그대로 가게 두었다 · 하이라이트 하나 — 1시간이 안 돼도)
            show.SetAwayMode(AwayMode.Keep);
            show.Touched(); show.Background(true); Idle(1.5);
            Scene("외판 구멍"); show.FastForward(Time.Minutes(5));
            Idle(121);
            show.Background(false);
            var back = show.AwayCard;
            bool window = back.Length > 0 && back[0].StartsWith("그사이 — 5분이 지났다") && back.Any(l => l.StartsWith("장면 ▶") && l.Contains("외판 구멍"));
            string text = string.Join(" ", card.Concat(back));
            bool words = !System.Text.RegularExpressions.Regex.IsMatch(text, "[A-Za-z{}_]") && !text.Any(ch => Hud.Missing.Contains(ch));
            Check("놓치지 않게 N6: 그사이 카드 — 손 없이 2분 + 게임 1시간 → 다음 손이 닿을 때 '그사이 — 1시간이 지났다 (때 → 때)' · 장 · 장면 · 숨진 이 (때 · 까닭) · 바뀐 것 (사람 6 → 5) · [닫기] / 손이 있었으면 · 바뀐 게 없으면 없다 / 창이 돌아올 때도 (하이라이트면 1시간이 안 돼도) · 개발 말 0",
                none0 && head && chapter && lost && changed && scenes && closed && touched && quiet && window && words,
                $"닿기 전 {none0} · 머리 {head} · 장 {chapter} · 숨진 이 {lost} · 바뀐 것 {changed} · 장면 {scenes} (하이라이트 {hl}) · 닫기 {closed} · 손 있음 {touched} · 조용 {quiet} · 창 {window} · 글 {words} [{string.Join(" | ", card)}] [{string.Join(" | ", back)}]");
        }

        // N7: 처음 한 줄 길잡이 — 첫 불 · 첫 ★에 한 번 (5초) · 둘째는 없다 · 다음 항해 (설정을 읽어 띄운 새 배)에는 없다
        {
            string dir = Path.Combine(Path.GetTempPath(), "shipship-prefs-" + Environment.ProcessId);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, ViewPrefs.FileName);
            var show = new LiveShow(HandShip(115), "디딤호") { FrameBudget = 1000 };
            var s = show.Sim; var L = s.Layout;
            int bed = L.Furniture.First(x => x.Type == "Bed").Cells[0];
            show.SetFixed(3); show.Advance(0.05);
            string Fire(LiveShow x) { var w = x.Sim; w.Facts.Emit(w.Tick, FactKind.Ignition, bed, 0, w.Causes.Add(CauseKind.Setup, "시험 — 불씨", w.Tick, bed)); x.Advance(0.2); return x.Hint; }
            string Star(LiveShow x) { var w = x.Sim; x.Dir.Highlights.Add(new Highlight { Tick = w.Tick, StartTick = w.Tick, Last = w.Tick, Cell = bed, Kind = FactKind.Ignition, Title = "침실 불이 번졌다" }); x.Advance(0.2); return x.Hint; }
            void Wait(LiveShow x, double sec) { x.Paused = true; x.Advance(sec); x.Paused = false; }
            string fire1 = Fire(show);
            var hf = show.Render();
            bool shown = hf.Hint == fire1 && hf.HintAlpha > 0.5f;
            Wait(show, 5.2);
            bool gone = show.Hint.Length == 0;
            string fire2 = Fire(show);
            string star1 = Star(show);
            Wait(show, 5.2);
            string star2 = Star(show);
            bool saved = show.Prefs.Dirty && show.Prefs.Save(path) && !show.Prefs.Dirty;
            var next = new LiveShow(HandShip(116), "디딤호") { FrameBudget = 1000, Prefs = ViewPrefs.Load(path) };
            next.SetFixed(3); next.Advance(0.05);
            string fire3 = Fire(next), star3 = Star(next);
            bool kept = next.Prefs.HintsSeen.Contains("fire") && next.Prefs.HintsSeen.Contains("star");
            // 깨진 설정 파일 → 새 설정 (예외 없음) · 앞 판 설정 (갈래 셋) → 빠진 갈래는 기본
            File.WriteAllText(path, "{ 깨짐");
            bool broken = ViewPrefs.Load(path).Stops.SequenceEqual(ViewPrefs.StopDefaults);
            var old = ViewPrefs.FromJson("{ \"V\": 1, \"Stops\": [false, true, true], \"Away\": 0, \"Bogus\": 3 }");
            bool older = old.Stops.Length == 5 && !old.Stops[0] && old.Stops[2] && !old.Stops[3] && old.AwayIs == AwayMode.Keep;
            try { Directory.Delete(dir, true); } catch (IOException) { }
            var texts = LiveShow.HintTexts.Select(x => x.text).ToList();
            bool words = texts.All(t => !System.Text.RegularExpressions.Regex.IsMatch(t.Replace("(X)", "").Replace("(L)", "").Replace("(Y)", "").Replace("(B)", "").Replace("(O)", ""), "[A-Za-z{}_]") && !t.Any(ch => Hud.Missing.Contains(ch)));
            Check("놓치지 않게 N7: 길잡이 — 첫 불 · 첫 ★에 한 번 (5초 뒤 사라진다) · 둘째는 없다 · 다음 항해 (설정을 읽어 띄운 배)에는 없다 · 깨진 설정 · 앞 판 설정도 읽힌다 · 개발 말 0",
                fire1.Contains("불") && shown && gone && fire2.Length == 0 && star1.Contains("★") && star2.Length == 0 && saved && fire3.Length == 0 && star3.Length == 0 && kept && broken && older && words,
                $"첫 불 '{fire1}' (보임 {shown} · 사라짐 {gone}) · 둘째 불 '{fire2}' · 첫 ★ '{star1}' · 둘째 '{star2}' · 저장 {saved} · 다음 항해 '{fire3}' '{star3}' (남음 {kept}) · 깨짐 {broken} · 앞 판 {older} · 글 {words}");
        }

        // Z2 (V7 몫): 큰 일 멈춤 · 길잡이는 산 세계 사실만 — 미리 보기 세계 (연출의 앞선 세계)에 숨짐 · 쓰러짐 · 외판 구멍을 넣어도 멈추지 않는다 (연출은 그 일을 본다) / 산 세계에 넣으면 멈춘다
        {
            var show = new LiveShow(HandShip(117), "디딤호") { FrameBudget = 1000, Prefs = new ViewPrefs() };
            show.Dir.AheadInBackground = false;
            var s = show.Sim; var m = s.Get<Mind>(); var L = s.Layout; var aim = HandTargets(s);
            int a0 = m.Agents.First(a => a.IsPerson).Id;
            s.Get<Hand>().Enqueue(Order(HandKind.Fire, aim.Bed, 2));   // 불 (기본 끔) — 연출이 미리 보기 시작한다
            show.SpeedOverride = Director.CalmSpeed;   // 조용함 빠르기 (미리 보기는 그대로 — 고정 빠르기로 빨리 감을 때만 쉰다)
            var sm = s.Get<ShipMind>();
            void Step() { sm.WaitThoughts(); show.Advance(0.05); }   // 주컴퓨터 일꾼의 셈을 받아 둔 뒤 (시험은 화면보다 빨라 일꾼을 기다린 장이 서 있다)
            for (int i = 0; i < 100 && show.Dir.Ahead == null; i++) Step();
            var ah = show.Dir.Ahead;
            int pred0 = show.Dir.Predicted, stops = 0;
            if (ah != null)
            {
                int c = ah.Causes.Add(CauseKind.Setup, "미리 본 일", ah.Tick, ah.Get<Mind>()[a0].C.Cell);
                ah.Facts.Emit(ah.Tick, FactKind.Collapsed, ah.Get<Mind>()[a0].C.Cell, 0, c, ah.Get<Mind>()[a0].C.Body);
                ah.Facts.Emit(ah.Tick, FactKind.Died, ah.Get<Mind>()[a0].C.Cell, 0, c, ah.Get<Mind>()[a0].C.Body);
                ah.Facts.Emit(ah.Tick, FactKind.WallBreach, aim.Hull, 0.3f, c);
            }
            for (int i = 0; i < 6; i++) { Step(); if (show.StopLine.Length > 0) stops++; }
            bool seen = show.Dir.Predicted > pred0;
            int lc = s.Causes.Add(CauseKind.Setup, "산 세계의 일", s.Tick, m[a0].C.Cell);
            s.Facts.Emit(s.Tick, FactKind.Collapsed, m[a0].C.Cell, 0, lc, m[a0].C.Body);
            for (int i = 0; i < 10 && show.StopLine.Length == 0; i++) Step();
            bool live = show.StopLine.Contains("쓰러졌다");
            Check("놓치지 않게 Z2 (큰 일 멈춤 몫): 산 세계 사실만 — 미리 보기 세계에 쓰러짐 · 숨짐 · 외판 구멍을 넣어도 멈추지 않는다 (연출은 미리 본다) / 산 세계에 넣으면 멈춘다",
                ah != null && stops == 0 && seen && live, $"미리 보기 {(ah != null ? ah.Tick - s.Tick : -1)}틱 앞 · 멈춤 {stops} · 연출이 봄 {seen} ({pred0} → {show.Dir.Predicted}) · 산 세계 '{show.StopLine}'");
        }

        // M5: 자리표 — 멈춤 카드 ([보기] [계속]) + ★ 띠 + 알림 + 길잡이 · 그사이 카드 ([닫기]) · 메뉴 [알림 멈춤 ›] [창이 뒤로 가면 ›] · 갈래 고르기 (다섯 · 셋) — 여섯 창 × 사람 3 · 12 · 40 · 100 × 글 100 · 150%:
        //     겹침 0 · 밖 0 · 누를 자리 크기 · 뒤로 차례 (그사이 카드는 뒤로 닫는다 · 멈춤 카드는 계속으로만) · 글자
        {
            var hud = new Hud(); var bad = new List<string>(); int layouts = 0;
            string[] away = { "그사이 — 9시간이 지났다 (3일째 22:10 → 4일째 07:30)", "장 · 3일째 — 박서연을 잃은 날", "장면 ▶ 07:04 침대 불이 번졌다 · ▶ 07:31 식당에서 다툼이 났다 · ▶ 08:02 외판 구멍", "숨진 이 · 박서연 (07:04 · 연기가 찼다)", "바뀐 것 · 식량 41 → 38일 · 사람 6 → 5 · 외판 구멍 0 → 1" };
            foreach (var (pw, ph) in new[] { (1920, 1080), (1920, 1010), (1600, 900), (1366, 768), (1280, 720), (2560, 1440) })
                foreach (float scale in new[] { 1f, 1.5f })
                    foreach (int n in new[] { 3, 12, 40, 100 })
                    {
                        string tag = $"{pw}×{ph} {scale * 100:0}% 사람 {n}";
                        Hud.Measure ms = (t, px, b) => Hud.Approx(t, px, b) * scale; Hud.MeasureLines ml = (t, px, w, k) => Hud.ApproxLines(t, (int)(px * scale), w, k);
                        foreach (bool panel in new[] { false, true })
                        {
                            // 멈춤 카드 + ★ 띠 + 알림 + 길잡이 (큰 일이 난 그 틱)
                            var f = FakeFrame(n, true, panel);
                            f.StopLine = "멈춤 — 김하늘이 쓰러졌다 (식당 · 07:04)"; f.Paused = true;
                            f.Banner = "식당 불이 번졌다 — 김하늘이 쓰러졌다"; f.BannerAlpha = 1;
                            f.Notice = "☆ 김하늘이 쓰러졌다"; f.NoticeLink = PanelKind.People;
                            f.Hint = LiveShow.HintTexts[2].text; f.HintAlpha = 1;
                            f.StopKinds = ViewPrefs.StopDefaults;
                            HudLayout.Build(f, new HudState(), pw, ph, ms, ml, hud); layouts++;
                            var card = hud.Area("멈춤 카드");
                            var see = hud.Hits.FirstOrDefault(x => x.Act == UiAct.StopSee); var go = hud.Hits.FirstOrDefault(x => x.Act == UiAct.StopGo);
                            if (card.Empty || see == null || go == null || !InsideR(see.R, card) || !InsideR(go.R, card) || see.R.Intersects(go.R) || see.R.H < 32 || go.R.H < 32 || see.R.W < 80 || go.R.W < 80) bad.Add($"{tag} 멈춤 카드 {card} · 보기 {see?.R} · 계속 {go?.R}");
                            if (hud.Area("길잡이").Empty || hud.Area("★ 띠").Empty || hud.Area("알림").Empty) bad.Add($"{tag} 길잡이 {hud.Area("길잡이")} · ★ {hud.Area("★ 띠")} · 알림 {hud.Area("알림")}");
                            else if (!(card.Bottom <= hud.Area("★ 띠").Y && hud.Area("★ 띠").Bottom <= hud.Area("알림").Y && hud.Area("알림").Bottom <= hud.Area("길잡이").Y)) bad.Add($"{tag} 차례: 멈춤 → ★ → 알림 → 길잡이");
                            var hs = new HudState();
                            if (hs.Back(f) == BackStep.CloseCard) bad.Add($"{tag} 멈춤 카드를 뒤로 닫았다");
                            foreach (var o in hud.Overlaps()) bad.Add($"{tag} 멈춤: {o}");
                            foreach (var o in hud.Outside()) bad.Add($"{tag} 멈춤: 밖 {o}");
                            // 그사이 카드 + 알림 + 길잡이
                            var fa = FakeFrame(n, true, panel);
                            fa.AwayCard = away; fa.Notice = "고정 빠르기 ×60"; fa.Hint = LiveShow.HintTexts[3].text; fa.HintAlpha = 1;
                            HudLayout.Build(fa, new HudState(), pw, ph, ms, ml, hud); layouts++;
                            var ac = hud.Area("그사이"); var close = hud.Hits.FirstOrDefault(x => x.Act == UiAct.AwayClose);
                            if (ac.Empty || close == null || !InsideR(close.R, ac) || close.R.H < 32 || close.R.W < 80) bad.Add($"{tag} 그사이 {ac} · 닫기 {close?.R}");
                            if (new HudState().Back(fa) != BackStep.CloseCard) bad.Add($"{tag} 그사이 카드를 뒤로 닫지 않는다");
                            foreach (var o in hud.Overlaps()) bad.Add($"{tag} 그사이: {o}");
                            foreach (var o in hud.Outside()) bad.Add($"{tag} 그사이: 밖 {o}");
                        }
                        // 메뉴 · 갈래 고르기 (알림 멈춤 다섯 · 창이 뒤로 가면 셋)
                        foreach (var open in new[] { HudOpen.Menu, HudOpen.Stops, HudOpen.Away })
                        {
                            var fm = FakeFrame(n, true);
                            fm.StopKinds = new[] { true, false, true, false, true }; fm.AwayMode = 1;
                            var st = new HudState { Open = open };
                            HudLayout.Build(fm, st, pw, ph, ms, ml, hud); layouts++;
                            var menu = hud.Area("목록 메뉴");
                            var items = hud.Hits.Where(x => x.Act is UiAct.MenuStops or UiAct.MenuAway).ToList();
                            if (items.Count != 2 || items.Any(x => !InsideR(x.R, menu) || x.R.H < 32)) bad.Add($"{tag} 메뉴 [알림 멈춤 ›] [창이 뒤로 가면 ›] {items.Count}");
                            if (open == HudOpen.Menu) continue;
                            var list = hud.Area(open == HudOpen.Stops ? "목록 알림 멈춤" : "목록 창이 뒤로 가면");
                            var picks = hud.Hits.Where(x => x.Act == (open == HudOpen.Stops ? UiAct.StopPick : UiAct.AwayPick)).OrderBy(x => x.Key).ToList();
                            int want = open == HudOpen.Stops ? 5 : 3;
                            bool on = open == HudOpen.Stops ? picks.Select(x => x.On).SequenceEqual(fm.StopKinds) : picks.Count == 3 && picks[1].On && !picks[0].On && !picks[2].On;
                            if (list.Empty || picks.Count != want || !on || picks.Any(x => !InsideR(x.R, list) || x.R.H < 32 || x.R.W < 160) || list.Intersects(menu)) bad.Add($"{tag} {open}: 줄 {picks.Count}/{want} · 켬 {on} · {list}");
                            st.Press(new Route(RouteKind.Hit, picks.FirstOrDefault()));
                            if (st.Open != open) bad.Add($"{tag} {open}: 고르니 닫혔다");
                            foreach (var o in hud.Outside()) bad.Add($"{tag} {open}: 밖 {o}");
                        }
                    }
            var texts = ViewPrefs.StopNames.Concat(ViewPrefs.AwayNames).Concat(LiveShow.HintTexts.Select(x => x.text)).Concat(HudLayout.MenuWords).Append(HudLayout.StopsHead).Append(HudLayout.AwayHead).ToList();
            var glyph = texts.Where(t => t.Any(ch => Hud.Missing.Contains(ch))).ToList();
            Check("놓치지 않게 M5: 멈춤 카드 ([보기] [계속]) → ★ 띠 → 알림 → 길잡이 차례 · 그사이 카드 ([닫기] · 뒤로 닫는다 — 멈춤 카드는 계속으로만) · 메뉴 [알림 멈춤 ›] [창이 뒤로 가면 ›] · 갈래 고르기 (다섯 · 셋 — 고르는 동안 열려 있다) — 여섯 창 × 사람 3 · 12 · 40 · 100 × 글 100 · 150% × 판: 겹침 0 · 밖 0 · 누를 자리 크기 · 글자",
                bad.Count == 0 && glyph.Count == 0, $"자리표 {layouts}가지 · 어긋남 {bad.Count} {string.Join(" | ", bad.Take(3))} · 글자 {string.Join(" ", glyph)}");
        }
    }
}
