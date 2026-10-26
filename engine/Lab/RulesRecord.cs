using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>
/// 기록 (docs/OBSERVER.md §16 G1 ~ G5 · M5 — 묶음 '기록'): 날이 바뀌면 장 카드 4초 (한 주 6초) · 누르면 기록 그 장 · 지나온 날 띠 (분위기 · 잃은 날 · 오늘 · 177일도 띠 안) ·
/// 장 펼침 (요약 · 장면 다섯 ● ○ ▶ · 주인공 얼굴 → 자세히) · 묘비 (함께한 날 · 한 일 · 추모 말 — 고리가 돌아도 · 원인 사슬) · 결산 (모두 잃음 · 임무 마침 · 그만둠 → 카드 · [그대로 지켜보기]는 세계를 잇는다 · 묶음 카드의 끝 갈래) ·
/// 자리표 (탭 여덟 · 기록 판 · 장 카드 · 결산 카드 — 여섯 창 × 사람 × 글 100 · 150%). 세계는 그대로 (화면 · 연출 장부만).
/// </summary>
public static partial class Program
{
    private static void RecordRules()
    {
        if (!Do("기록")) return;
        Console.WriteLine("\n[기록 — 장 카드 · 날 띠 · 장 펼침 · 묘비 · 결산 · 자리표]");
        if (Environment.GetEnvironmentVariable("RECORD_BENCH") == "1")
        {
            // 재기 (판정하지 않는다): 디딤호 한 시간 — 오늘 장 · 177일 기록 판 줄 · 날 띠
            var b = new LiveShow(HandShip(69), "디딤호"); b.FastForward(Time.Hours(1)); b.Paused = true;
            for (int d = 0; d < 177; d++) { var c = new Chapter { Day = d, Title = $"{d + 1}일째 — 조용한 하루", Mood = d % 4 }; c.Lines.Add("하루의 결 — 조용함"); b.Dir.Chapters.Add(c); }
            b.Advance(0.01); b.TogglePanel(PanelKind.Record);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 50; i++) b.Dir.ChapterNow(b.Sim);
            double now = sw.Elapsed.TotalMilliseconds / 50; sw.Restart();
            for (int i = 0; i < 200; i++) b.DayStrip();
            double strip = sw.Elapsed.TotalMilliseconds / 200; sw.Restart();
            for (int i = 0; i < 50; i++) { b.PanelPickKey = LiveShow.KeyChapter(i); b.PanelRows(); }
            double rows = sw.Elapsed.TotalMilliseconds / 50; sw.Restart();
            for (int i = 0; i < 200; i++) b.PanelRows();
            double same = sw.Elapsed.TotalMilliseconds / 200;
            Console.WriteLine($"    (기록 재기) 오늘 장 {now:0.000}ms · 날 띠 177일 {strip * 1000:0}µs · 기록 판 줄 (새로 지음) {rows:0.000}ms · 그대로 {same * 1000:0.0}µs");
        }

        // G1: 날이 바뀐 틱 (연출의 장이 늘면) 장 카드 4초 · 한 주 장은 6초 · 같은 틱에 날 장과 한 주 장이 함께 나면 한 주 장 · 누르면 기록 그 장 (펼친 채)
        {
            var show = new LiveShow(HandShip(61), "디딤호");
            show.Paused = true;
            var m = show.Sim.Get<Mind>();
            var ch = new Chapter { Day = 0, Title = "1일째 — 정지우가 불을 껐다", Arc = "조용함 → 위험(07:04) → 조용함", Mood = 2 };
            ch.Lines.Add("큰 사슬 [불·화학 → 몸] 침대 불 → … → 불을 껐다"); ch.Lines.Add("하루의 결 — 조용함 → 위험(07:04) → 조용함");
            ch.Cast.Add(0); ch.Cast.Add(1);
            show.Dir.Chapters.Add(ch);
            show.Advance(0.1);
            var f0 = show.Render();
            double Seen(LiveShow s, double limit) { double t = 0; while (t < limit) { s.Advance(0.25); t += 0.25; if (s.Render().ChapterTitle.Length == 0) return t; } return t; }
            double dayLeft = Seen(show, 10);
            var wk = new Chapter { Day = 6, Title = "7일째 — 조용한 하루" };
            var week = new Chapter { Day = 6, Week = true, Title = "1주째 — 정지우가 불을 껐다", Mood = 2 };
            show.Dir.Chapters.Add(wk); show.Dir.Chapters.Add(week);
            show.Advance(0.1);
            var f1 = show.Render();
            double weekLeft = Seen(show, 10);
            show.DayTurned(ch);
            show.ChapterCardGo();
            var f2 = show.Render();
            var open = f2.PanelRows.FirstOrDefault(r => r.Key == LiveShow.KeyChapter(0));
            bool opened = f2.Panel == PanelKind.Record && f2.Detail == DetailKind.Record && f2.DetailSub == 0 && f2.PanelPickKey == LiveShow.KeyChapter(0) && open.Sub.Contains("큰 사슬");
            Check("기록 G1: 날이 바뀌면 (연출의 장이 늘면) 가운데 칸에 장 카드 — 제목 · 하루의 결 · 주인공 얼굴 · 4초 (한 주 장은 6초 · 같이 나면 한 주 장) · 누르면 기록 판 그 장이 펼쳐진다",
                f0.ChapterTitle == ch.Title && f0.ChapterLine.Contains("위험") && f0.ChapterFaces.Length == 2 && f0.ChapterAlpha > 0 && !f0.ChapterWeek && dayLeft is >= 3.9 and <= 4.6
                && f1.ChapterTitle == week.Title && f1.ChapterWeek && weekLeft is >= 5.9 and <= 6.6 && opened,
                $"날 '{f0.ChapterTitle}' · '{f0.ChapterLine}' · 얼굴 {f0.ChapterFaces.Length} · {dayLeft:0.00}초 / 주 '{f1.ChapterTitle}' {weekLeft:0.00}초 / 누름 → 판 {f2.Panel} · {f2.Detail} · 펼침 {opened} '{open.Sub}'");
        }

        // G2: 날 띠 — 날마다 한 칸 (그날 가장 높은 분위기 · 잃은 이가 있었나 · 오늘) · 누르면 그 장 / 177일도 띠 안 (넘치면 이레 칸)
        {
            var show = new LiveShow(HandShip(62), "디딤호");
            show.Paused = true;
            int[] moods = { 0, 3, 1 };
            for (int d = 0; d < 3; d++) { var c = new Chapter { Day = d, Title = $"{d + 1}일째 — 조용한 하루", Mood = moods[d] }; if (d == 1) c.Lost.Add(2); show.Dir.Chapters.Add(c); }
            var cells = show.DayStrip(Time.TicksPerDay * 3 + 100);
            bool shape = cells.Length == 4 && cells.Take(3).Select(c => c.Mood).SequenceEqual(moods) && cells.Select(c => c.Lost).SequenceEqual(new[] { false, true, false, false })
                && cells.Select(c => c.Today).SequenceEqual(new[] { false, false, false, true }) && cells[1].Key == LiveShow.KeyChapter(1) && cells[3].Day == 3;
            show.TogglePanel(PanelKind.Record);
            if (cells.Length > 1) show.DayCellGo(cells[1].Key);
            var f = show.Render();
            bool go = f.PanelPickKey == LiveShow.KeyChapter(1) && f.Days.Length >= 3;
            // 177일: 여섯 창 — 칸이 모두 띠 안 · 4도트 넘게 · 서로 겹치지 않는다 (넘치면 이레 칸)
            var hud = new Hud(); var bad = new List<string>(); int grouped = 0;
            foreach (var (pw, ph) in new[] { (1920, 1080), (1920, 1010), (1600, 900), (1366, 768), (1280, 720), (2560, 1440) })
                foreach (int days in new[] { 3, 40, 177 })
                {
                    var ff = RecordFrame(12, days);
                    HudLayout.Build(ff, new HudState(), pw, ph, Hud.Approx, Hud.ApproxLines, hud);
                    var strip = hud.Area("날 띠"); var panel = hud.Area("판");
                    string tag = $"{pw}×{ph} {days}일";
                    if (strip.Empty || !InsideR(strip, panel)) { bad.Add($"{tag}: 띠 {strip}"); continue; }
                    var dc = hud.DayCells;
                    if (dc.Count != days && dc.Count != (days + 6) / 7) bad.Add($"{tag}: 칸 {dc.Count}");
                    if (dc.Count < days) grouped++;
                    for (int i = 0; i < dc.Count; i++)
                    {
                        if (!InsideR(dc[i].R, strip) || dc[i].R.W < 3.99f) bad.Add($"{tag}: 칸 {i} {dc[i].R.X:0}+{dc[i].R.W:0.0}");
                        if (i > 0 && dc[i].R.X < dc[i - 1].R.Right - 0.01f) bad.Add($"{tag}: 칸 {i} 겹침");
                    }
                    if (dc.Count == 0 || !dc.Last().Today) bad.Add($"{tag}: 마지막 칸이 오늘이 아니다");
                    var hits = hud.Hits.Count(x => x.Act == UiAct.DayCell);
                    if (hits != dc.Count) bad.Add($"{tag}: 누를 자리 {hits}/{dc.Count}");
                }
            Check("기록 G2: 지나온 날 띠 — 날마다 한 칸 (그날 가장 높은 분위기 · 잃은 날 · 오늘은 마지막 칸) · 누르면 그 장 / 3 · 40 · 177일 × 여섯 창: 칸이 모두 띠 안 · 4도트 넘게 · 겹침 0 (넘치면 이레 칸)",
                shape && go && bad.Count == 0 && grouped > 0,
                $"칸 {string.Join(" ", cells.Select(c => $"{c.Day}:{c.Mood}{(c.Lost ? "+" : "")}{(c.Today ? "◆" : "")}"))} · 누름 {go} · 이레 칸 {grouped}가지 · 어긋남 {bad.Count} {string.Join(" | ", bad.Take(3))}");
        }

        // G3: 장 펼침 — 요약 줄 · 장면 (다시 볼 수 있으면 ● · 아니면 ○ · ▶) · 주인공 얼굴 → 누르면 그 사람 자세히 · ▶은 다시보기
        {
            var show = new LiveShow(HandShip(63), "디딤호");
            show.Advance(0.1); show.Paused = true;
            var m = show.Sim.Get<Mind>();
            int robot = m.Agents.First(a => !a.IsPerson).Id;
            var h1 = new Highlight { Tick = show.Sim.Tick, StartTick = show.Sim.Tick, Last = show.Sim.Tick, Cell = 0, Score = 120, Title = "침대 불 → 불을 껐다" };
            var h2 = new Highlight { Tick = -50, StartTick = -60, Last = -50, Cell = 0, Score = 90, Title = "말다툼" };
            show.Dir.Highlights.Add(h1); show.Dir.Highlights.Add(h2);
            var ch = new Chapter { Day = 0, Title = "1일째 — 불을 껐다", Mood = 3 };
            ch.Lines.Add("큰 사슬 [불·화학] 침대 불 → 불을 껐다"); ch.Lines.Add("주인공 — 둘");
            ch.Scenes.Add(h2); ch.Scenes.Add(h1); ch.Cast.Add(1); ch.Cast.Add(robot);
            show.Dir.Chapters.Add(ch);
            show.TogglePanel(PanelKind.Record);
            var closed = show.PanelRows().Count(r => r.Key != 0 && (r.Key & 0xFF) != 0 && r.Key >> 8 == LiveShow.KeyChapter(0) >> 8);
            show.PanelClick(LiveShow.KeyChapter(0));
            var rows = show.PanelRows();
            var head = rows.FirstOrDefault(r => r.Key == LiveShow.KeyChapter(0));
            var scenes = rows.Where(r => r.Act.Length > 0 && r.Key >> 8 == LiveShow.KeyChapter(0) >> 8).ToList();
            var faces = rows.Where(r => r.Face >= 0 && r.Key >> 8 == LiveShow.KeyChapter(0) >> 8).ToList();
            bool marks = scenes.Count == 2 && scenes[0].Main.StartsWith("○") && !scenes[0].ActOn && scenes[1].Main.StartsWith("●") && scenes[1].ActOn;
            bool faced = faces.Count == 1 && rows.Any(r => r.Key >> 8 == LiveShow.KeyChapter(0) >> 8 && r.Main.Contains("로봇") && r.Face < 0);
            bool person = faces.Count == 1 && show.PanelClick(faces[0].Key) && show.Detail == DetailKind.Person && show.DetailId == 1;
            show.ClosePanel(); show.TogglePanel(PanelKind.Record); show.PanelClick(LiveShow.KeyChapter(0));
            bool replay = scenes.Count == 2 && show.PanelAct(scenes[1].Key) && show.Replaying && show.ReplayOf == h1;
            if (show.Replaying) show.StopReplay();
            Check("기록 G3: 장을 누르면 펼친다 — 요약 줄 · 장면 (다시 볼 수 있으면 ● · 지난 것은 ○ · ▶ 다시보기) · 주인공 (사람은 얼굴 — 누르면 그 사람 자세히 · 로봇은 이름만) · 펼치기 전에는 머리 줄만",
                closed == 0 && (head.Sub ?? "").Contains("큰 사슬") && marks && faced && person && replay,
                $"닫힘 {closed} · 머리 '{head.Main}' / '{(head.Sub ?? "").Replace('\n', '/')}' · 장면 {string.Join(" | ", scenes.Select(r => $"{r.Main} {r.ActOn}"))} · 얼굴 {faces.Count} · 자세히 {person} · 다시보기 {replay}");
        }

        // G4: 묘비 — 함께한 날 · 한 일 (구함 · 끔 · 덧댐) · 원인 사슬 · 추모 말 (추모 모임 동안 — 말 기록 고리가 돌아도 남는다) · ▶ 그 장면
        {
            var s = HandShip(64);
            var show = new LiveShow(s, "디딤호");
            show.Paused = true;
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var so = s.Get<Society>();
            var people = m.Agents.Where(a => a.IsPerson).Select(a => a.Id).ToList();
            int dead = people[0], mourner = people[1];
            for (int i = 0; i < 3; i++) s.Step();
            show.Dir.Observe(s);
            int at = m[dead].C.Cell;
            void Emit(FactKind k, int actor, int cause = 0, float size = 0, int other = -1) => s.Facts.Emit(s.Tick, k, at, size, cause, actor, other);
            Emit(FactKind.Rescue, dead); Emit(FactKind.Rescue, dead); Emit(FactKind.Extinguished, dead); Emit(FactKind.Extinguished, dead); Emit(FactKind.Extinguished, dead); Emit(FactKind.Patched, dead);
            int c1 = s.Causes.Add(CauseKind.Setup, "침대 밑 불씨", s.Tick, at), c2 = s.Causes.Add(CauseKind.Fire, "연기가 찼다", s.Tick, at, c1), c3 = s.Causes.Add(CauseKind.Biology, "숨이 멎었다", s.Tick, at, c2);
            { ref var b = ref life[m[dead].C.Body]; b.Alive = 0; b.Conscious = 0; b.DiedAt = s.Tick; }
            s.Facts.Emit(s.Tick, FactKind.Died, at, 0, c3, m[dead].C.Body);
            s.Step(); show.Dir.Observe(s);
            s.Facts.Emit(s.Tick, FactKind.Meeting, at, (int)CouncilKind.Memorial, 0, mourner, dead);
            s.Step(); show.Dir.Observe(s);
            so.Sayings.Add(new Saying { Tick = s.Tick, Speaker = mourner, Listener = mourner, Topic = Topic.Death, End = TalkEnd.Comfort, Line = s.Causes.Add(CauseKind.Society, "늘 먼저 웃던 사람이었어", s.Tick, at), Cell = at });
            so.Sayings.Add(new Saying { Tick = s.Tick, Speaker = mourner, Listener = mourner, Topic = Topic.Work, End = TalkEnd.Agree, Line = s.Causes.Add(CauseKind.Society, "밥은 먹었어?", s.Tick, at), Cell = at });
            show.Dir.Observe(s);
            s.Step();
            s.Facts.Emit(s.Tick, FactKind.Culture, at, (int)CultureEvent.Memorial, 0, mourner, dead);
            show.Dir.Observe(s);
            for (int i = 0; i < 200; i++) { so.Sayings.Add(new Saying { Tick = s.Tick, Speaker = mourner, Topic = Topic.Work, End = TalkEnd.Agree }); if (so.Sayings.Count > 128) so.Sayings.RemoveAt(0); }   // 말 기록 고리가 돈다
            var h = new Highlight { Tick = s.Tick - 2, StartTick = s.Tick - 3, Last = s.Tick - 2, Cell = at, Score = 200, Kind = FactKind.Died, Actor = m[dead].C.Body, Title = "침대 밑 불씨 → … → 숨이 멎었다", Cause = c3 };
            show.Dir.Highlights.Add(h);
            show.TogglePanel(PanelKind.Record); show.SetDetailSub(1);
            var rows = show.PanelRows();
            var tomb = rows.FirstOrDefault(r => r.Main.StartsWith(m.NameOf(dead)));
            string name = m.NameOf(mourner);
            bool ok = tomb.Main != null && tomb.Sub.Contains("1일을 함께했다") && tomb.Sub.Contains("구한 사람 2") && tomb.Sub.Contains("끈 불 3") && tomb.Sub.Contains("덧댄 구멍 1")
                && tomb.Sub.Contains("침대 밑 불씨") && tomb.Sub.Contains("숨이 멎었다") && tomb.Sub.Contains($"추모 — {name}: \"늘 먼저 웃던 사람이었어\"") && !tomb.Sub.Contains("밥은 먹었어")
                && tomb.Face >= 0 && tomb.Act.Length > 0 && rows.Count(r => r.Face >= 0) == 1;
            bool none = new LiveShow(HandShip(65), "디딤호").Let(x => { x.TogglePanel(PanelKind.Record); x.SetDetailSub(1); return x.PanelRows().Any(r => r.Main.Contains("아무도 잃지 않았다")); });
            Check("기록 G4: 묘비 — '이름 · 맡은 일' · 숨진 때와 원인 사슬 · 'n일을 함께했다 · 구한 사람 2 · 끈 불 3 · 덧댄 구멍 1' · 추모 모임의 기리는 말 (말 기록 고리가 200마디 돌아도 남는다 · 다른 말은 아니다) · 얼굴 · ▶ 그 장면 / 잃은 이가 없으면 한 줄",
                ok && none, tomb.Main == null ? "묘비 없음" : $"'{tomb.Main}' / '{tomb.Sub.Replace('\n', '/')}' · 얼굴 {tomb.Face} · 단추 '{tomb.Act}' · 잃은 이 없음 줄 {none}");
        }

        // G5: 결산 — 모두 잃음 · 임무 마침 · 함장이 그만둠 → 카드 (멈춘다 · 사람마다 한 줄) · 묶음 카드에 끝 갈래 · [그대로 지켜보기]는 세계를 잇는다 (다시 뜨지 않는다) · 이어 봐도 다시 뜨지 않는다
        {
            var lost = new LiveShow(HandShip(66), "디딤호");
            { var m = lost.Sim.Get<Mind>(); var life = lost.Sim.Get<Life>(); foreach (var a in m.Agents.Where(a => a.IsPerson)) { ref var b = ref life[a.C.Body]; b.Alive = 0; b.Conscious = 0; b.DiedAt = lost.Sim.Tick; } }
            lost.Advance(0.05);
            var fl = lost.Render();
            var meta = lost.SaveBundle(SaveKind.End).Meta;
            var back = LiveShow.Resume(lost.SaveBundle(SaveKind.End));
            back.Advance(0.05);
            bool backQuiet = back.Ended == VoyageEnd.AllLost && back.EndCard.Length == 0;
            long t0 = lost.Sim.Tick;
            lost.EndWatch();
            for (int i = 0; i < 4; i++) lost.Advance(0.25);
            bool goes = lost.Sim.Tick > t0 && !lost.Paused && lost.EndCard.Length == 0 && lost.Ended == VoyageEnd.AllLost;
            bool lostOk = lost.Ended == VoyageEnd.AllLost && fl.EndCard.Length > 2 && fl.EndCard[0].Contains("모두 잃었다") && fl.Paused && meta.End == "모두 잃었다";

            var done = new LiveShow(HandShip(67), "디딤호");
            var sm = done.Sim.Get<ShipMind>(); sm.Mission.Clear(); sm.MissionsDone = 1;
            done.Advance(0.05);
            var fd = done.Render();
            int persons = done.Sim.Get<Mind>().Agents.Count(a => a.IsPerson);
            bool doneOk = done.Ended == VoyageEnd.Mission && fd.EndCard.Length > 0 && fd.EndCard[0].Contains("임무를 마쳤다") && fd.EndCard.Count(l => l.Contains("살았다")) == persons;

            var quit = new LiveShow(HandShip(68), "디딤호");
            quit.Advance(0.05);
            bool calm = quit.Ended == VoyageEnd.None && quit.Render().EndCard.Length == 0;
            quit.EndVoyage(VoyageEnd.Quit);
            var fq = quit.Render();
            bool quitOk = calm && fq.EndCard.Length > 0 && fq.EndCard[0].Contains("함장이 그만두었다") && quit.SaveBundle(SaveKind.End).Meta.End == "함장이 그만두었다" && fq.Ended;
            bool due = quit.EndSaveDue;
            Check("기록 G5: 결산 — 산 사람 0 · 임무를 마침 · 함장이 그만둠 → 가운데 결산 카드 (멈춘다 · '디딤호 · 항해 n — …' · 사람마다 한 줄) · 묶음 카드의 끝 갈래 · 저장할 때 · [그대로 지켜보기]는 세계를 잇고 다시 뜨지 않는다 · 이어 본 항해에도",
                lostOk && goes && backQuiet && doneOk && quitOk && due,
                $"모두 잃음 {lostOk} '{(fl.EndCard.Length > 0 ? fl.EndCard[0] : "")}' · 카드 끝 '{meta.End}' · 지켜보기 {goes} ({t0}→{lost.Sim.Tick}) · 이어 봄 {backQuiet} / 임무 {doneOk} '{(fd.EndCard.Length > 0 ? fd.EndCard[0] : "")}' 산 이 줄 {fd.EndCard.Count(l => l.Contains("살았다"))}/{persons} / 그만둠 {quitOk} · 저장 {due}");
        }

        // M5: 탭 여덟 · 기록 판 (날 띠) · 장 카드 · 결산 카드 · 메뉴 [항해 끝내기] — 여섯 창 × 사람 3 · 12 · 40 · 100 × 글 100 · 150% — 겹침 0 · 밖 0 · 누를 자리 크기 · 뒤로 · 글자
        {
            var hud = new Hud();
            var sizes = new[] { (1920, 1080), (1920, 1010), (1600, 900), (1366, 768), (1280, 720), (2560, 1440) };
            var bad = new List<string>(); var small = new List<string>(); int layouts = 0;
            bool tabs = HudLayout.TabNames.Length == 8 && HudLayout.TabNames[7] == "기록" && HudLayout.TabKinds[7] == PanelKind.Record && HudLayout.TabKeys[7] == "Y";
            foreach (var (pw, ph) in sizes)
                foreach (int n in new[] { 3, 12, 40, 100 })
                    foreach (float scale in new[] { 1f, 1.5f })
                    {
                        Hud.Measure ms = (t, px, b) => Hud.Approx(t, px, b) * scale;
                        Hud.MeasureLines ml = (t, px, w, k) => Hud.ApproxLines(t, (int)(px * scale), w, k);
                        string tag = $"{pw}×{ph} 사람 {n} 글 {scale * 100:0}%";
                        // 기록 판 + 장 카드 + ★ 띠 + 알림 (가운데 칸이 가장 붐빌 때)
                        {
                            var f = RecordFrame(n, 177);
                            f.ChapterTitle = "43일째 — 김하늘과 정지우가 짝이 되었다"; f.ChapterLine = "조용함 → 조짐 4번 → 결정적 순간(07:04) → 조용함"; f.ChapterFaces = new[] { 0, 1, 2 }; f.ChapterAlpha = 1;
                            f.Banner = "식당 불 → 연기 → 박서연이 쓰러졌다"; f.BannerAlpha = 1; f.CanReplay = true; f.Notice = "주컴퓨터가 일지를 썼다 — 눌러서 보기"; f.NoticeLink = PanelKind.Journal;
                            HudLayout.Build(f, new HudState(), pw, ph, ms, ml, hud); layouts++;
                            var tab = hud.Hits.Where(x => x.Act == UiAct.Tab).ToList();
                            if (tab.Count != 8 || !tab.Any(x => x.Key == 7 && x.On)) bad.Add($"{tag}: 탭 {tab.Count} · 기록 켜짐 {tab.Any(x => x.Key == 7 && x.On)}");
                            var card = hud.Area("장 카드"); var go = hud.Hits.FirstOrDefault(x => x.Act == UiAct.ChapterCard);
                            if (card.Empty || go == null) bad.Add($"{tag}: 장 카드 {card} · 누를 자리 {go != null}");
                            else { Sizes(tag + " 장 카드", new[] { go }); if (card.Right > hud.Area("판").X - 8) bad.Add($"{tag}: 장 카드가 판에 닿는다"); }
                            foreach (var o in hud.Overlaps()) bad.Add($"{tag}: {o}");
                            foreach (var o in hud.Outside()) bad.Add($"{tag}: 밖 {o}");
                            var sub = hud.Hits.Where(x => x.Act == UiAct.DetailTab).ToList();
                            if (sub.Count != 4) bad.Add($"{tag}: 작은 탭 {sub.Count}");
                            Sizes(tag + " 작은 탭", sub, 64, 30);
                        }
                        // 결산 카드 (판이 닫힌 채 · 판이 열린 채)
                        foreach (bool panel in new[] { false, true })
                        {
                            var f = panel ? RecordFrame(n, 43) : FakeFrame(n, true, false);
                            f.EndCard = new[] { "디딤호 · 항해 4127 — 43일 · 임무를 마쳤다" }.Concat(Enumerable.Range(0, 12).Select(i => i % 3 == 0 ? $"김하늘{i} — 17일째 — 연기에 숨졌다" : $"김하늘{i} — 살았다 — 구한 사람 2 · 끈 불 3")).Concat(new[] { "외 4명", "큰 장면 — 3일 07:04 식당 불 → 박서연이 쓰러졌다" }).ToArray();
                            f.Paused = true; f.Ended = true;
                            HudLayout.Build(f, new HudState(), pw, ph, ms, ml, hud); layouts++;
                            var card = hud.Area("결산 카드");
                            var btn = hud.Hits.Where(x => x.Act is UiAct.EndNew or UiAct.EndWatch).ToList();
                            if (card.Empty || btn.Count != 2 || btn.Any(b => !InsideR(b.R, card))) bad.Add($"{tag} 결산 {(panel ? "판" : "")}: 카드 {card} · 단추 {btn.Count}");
                            Sizes(tag + " 결산", btn);
                            if (btn.Count == 2 && btn[0].R.Intersects(btn[1].R)) bad.Add($"{tag} 결산: 단추가 겹친다");
                            foreach (var o in hud.Overlaps()) bad.Add($"{tag} 결산: {o}");
                            foreach (var o in hud.Outside()) bad.Add($"{tag} 결산: 밖 {o}");
                        }
                    }
            // 메뉴 [항해 끝내기] — 끝난 항해면 옅게 (까닭) · 확인 창 · 뒤로는 결산 카드부터
            var fm = FakeFrame(3, true); var sm0 = new HudState { Open = HudOpen.Menu };
            HudLayout.Build(fm, sm0, 1920, 1080, Hud.Approx, Hud.ApproxLines, hud);
            var end0 = hud.Hits.FirstOrDefault(x => x.Act == UiAct.MenuEnd);
            fm.Ended = true;
            HudLayout.Build(fm, sm0, 1920, 1080, Hud.Approx, Hud.ApproxLines, hud);
            var end1 = hud.Hits.FirstOrDefault(x => x.Act == UiAct.MenuEnd);
            bool menu = end0 != null && end0.Enabled && end1 != null && !end1.Enabled && end1.Why.Length > 0;
            var ask = new HudState { Ask = HudAsk.End };
            HudLayout.Build(FakeFrame(3, true), ask, 1920, 1080, Hud.Approx, Hud.ApproxLines, hud);
            bool askOk = hud.Modal && hud.Hits.Any(x => x.Act == UiAct.AskYes) && ask.Back(FakeFrame(3, true)) == BackStep.CloseAsk;
            var fe = FakeFrame(3, true); fe.EndCard = new[] { "디딤호 · 항해 4127 — 3일 · 모두 잃었다" }; fe.Armed = HandKind.Fire;
            var back = new HudState().Back(fe);
            var texts = LiveShow.RecordTabs.Concat(new[] { LiveShow.EndText(VoyageEnd.AllLost), LiveShow.EndText(VoyageEnd.Mission), LiveShow.EndText(VoyageEnd.Quit), HudLayout.EndButton(true), HudLayout.EndButton(false), HudLayout.ChapterHint, HudLayout.AskText(HudAsk.End) }).ToList();
            var glyph = texts.Where(t => t.Any(c => Hud.Missing.Contains(c)) || System.Text.RegularExpressions.Regex.IsMatch(t, "[A-Za-z{}_]")).ToList();
            Check("기록 M5: 탭 여덟 ('기록' · Y) · 기록 판 (작은 탭 넷 · 날 띠 177일) · 장 카드 (★ 띠 · 알림과 함께) · 결산 카드 ([같은 배로 새 항해] [그대로 지켜보기] — 판이 닫힌 채 · 열린 채) — 여섯 창 × 사람 3 · 12 · 40 · 100 × 글 100 · 150%: 겹침 0 · 밖 0 · 누를 자리 크기 / 메뉴 [항해 끝내기] (끝난 항해면 옅게) · 확인 창 · 뒤로는 결산 카드가 겨눔보다 먼저 · 글자",
                tabs && bad.Count == 0 && small.Count == 0 && menu && askOk && back == BackStep.CloseCard && glyph.Count == 0,
                $"탭 {tabs} · 자리표 {layouts}가지 · 어긋남 {bad.Count} {string.Join(" | ", bad.Take(3))} · 작은 자리 {small.Count} {string.Join(" | ", small.Take(2))} · 메뉴 {menu} · 확인 {askOk} · 뒤로 {back} · 글자 {string.Join(" ", glyph)}");

            void Sizes(string tag, IEnumerable<UiHit> hits, float minW = 32, float minH = 32)
            {
                foreach (var x in hits) if (x.R.W + 0.01f < minW || x.R.H + 0.01f < minH) small.Add($"{tag}: {x.Act}/{x.Key} {x.R.W:0}×{x.R.H:0}");
            }
        }
    }

    private static bool InsideR(RectF inner, RectF outer) => inner.X >= outer.X - 0.5f && inner.Y >= outer.Y - 0.5f && inner.Right <= outer.Right + 0.5f && inner.Bottom <= outer.Bottom + 0.5f;

    /// <summary>기록 판이 열린 시험용 그림 (날마다 · 지나온 날 days칸 — 마지막은 오늘 · 장 줄 서른).</summary>
    private static LiveFrame RecordFrame(int people, int days)
    {
        var f = FakeFrame(people, true);
        f.Panel = PanelKind.Record; f.Detail = DetailKind.Record; f.DetailTabs = LiveShow.RecordTabs; f.DetailSub = 0; f.DetailId = -1; f.DetailFace = -1;
        f.PanelTitle = "기록"; f.DetailLine = $"디딤호 · 항해 4127 · {days}일째 · 지나온 장 {days - 1}";
        f.Days = Enumerable.Range(0, days).Select(d => new DayCell(d, d % 4, d % 9 == 4, d == days - 1, LiveShow.KeyChapter(d))).ToArray();
        f.PanelRows = Enumerable.Range(0, 30).Select(i => new PanelRow($"{43 - i}일째 — 정지우가 불을 껐다", i == 0 ? "큰 사슬 [불·화학 → 몸] 침대 불 → … → 불을 껐다\n하루의 결 — 조용함 → 위험(07:04) → 조용함" : "", false, Key: LiveShow.KeyChapter(i))).ToArray();
        f.PanelPick = 0; f.PanelPickKey = f.PanelRows[0].Key; f.PanelFoot = "줄을 누르면 펼친다 · 띠의 칸을 누르면 그날 · 휠로 넘기기 (↑↓ · Y)";
        return f;
    }
}

internal static class LetExt
{
    public static TR Let<T, TR>(this T x, Func<T, TR> f) => f(x);
}
