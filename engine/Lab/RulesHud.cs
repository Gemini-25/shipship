using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>마우스만으로 되는 화면 (docs/UI.md 8 — 함장 요청 10-23): 자리표 · 누름 · 뒤로 · 판 줄 열쇠 · 출항 준비 · 커서 쪽 확대 · 올림과 누름 · 넘기기 — 세계는 그대로.</summary>
public static partial class Program
{
    /// <summary>자리표 시험용 그림 한 장 (사람 n · 로봇 n/3 · 모두 켬 / 모두 끔 · 판을 닫으면 말 창).</summary>
    private static LiveFrame FakeFrame(int people, bool on, bool panel = true)
    {
        var f = new LiveFrame
        {
            Status = "다시보기 · 한누리호 · 3일 14:22 · 결정적 순간 · ×120 · 하루 6분 · 사람 100/100 · 따라 보기 (천천히)",
            Vitals = new[]
            {
                new Gauge("air", "공기", "산소 20.8kPa · 이산화탄소 0.6kPa · 일산화탄소 30ppm", 1), new Gauge("crew", "사람", "100/100 · 쓰러짐 12 · 다침 34", 2),
                new Gauge("power", "전력", "182/240kW", 0), new Gauge("hull", "선체", "구멍 둘 · 빠지는 방 하나", 2), new Gauge("supply", "물자", "식량 넉넉 · 물 넉넉", 0), new Gauge("fire", "불", "셋 · 주컴퓨터는 아직 모른다", 2),
            },
            Crew = Enumerable.Range(0, people).Select(i => new CrewFace(i, "김하늘" + i, "불을 끈다", i % 4, i)).ToArray(),
            Robots = Enumerable.Range(0, Math.Max(1, people / 3)).Select(i => new RobotChip(people + i, (i % 5 == 4 ? "D" : "R") + (i + 1), i % 2 == 0 ? "충전" : "불", i % 4, "로봇")).ToArray(),
        };
        if (!on) return f;
        if (!panel) f.Talk = new[] { new TalkLine("09:12 로봇 2 → 식당 불", 1, 1), new TalkLine("09:12 식당 봉쇄 (사람이 안에 있다)", 2, 1), new TalkLine("09:13 식당 히터 전원을 내렸다 — 불이 났다", 0, 1) };
        f.Card = new[] { "김하늘 '불꽃' · 기관사", "사람됨: 말이 많고 용감하다 · 꿈: 귀향 · 즐겨 하는 것: 음악을 듣는다 · 짝: 이도윤", "생각: 불이야! 다들 다치면 안 되는데", "몸: 깨어 있음 · 핏속 산소 95% · 체온 37.0℃", "다침: 화상 12", "마음: 두려움 · 긴장" };
        f.Selected = 0;
        f.Feed = Enumerable.Range(0, 5).Select(i => new FeedItem("09:1" + i, "식당 불 — 주컴퓨터: 로봇 2", i % 3, i * 10, true)).ToArray();
        f.Panel = PanelKind.People; f.PanelTitle = "사람";
        f.PanelRows = Enumerable.Range(0, 30).Select(i => new PanelRow("이름 " + i + " — 불을 끈다", "사람됨: …\n생각: …\n몸: 산소 97%", false, i, new[] { new PanelBar("숨", 0.9f, 0) }, LiveShow.KeyPerson(i))).ToArray();
        f.PanelPick = 0; f.PanelPickKey = f.PanelRows[0].Key; f.PanelFoot = "사람을 누르면 따라간다 · 휠로 넘기기 (↑↓ · Enter · P)";
        f.Banner = "식당에서 불이 났다 → 김하늘이 불을 껐다 · 로봇 2가 거들었다"; f.BannerAlpha = 1; f.CanReplay = true;
        f.Notice = "주컴퓨터가 일지를 썼다 — 눌러서 보기"; f.NoticeLink = PanelKind.Journal;
        f.Broadcast = "주컴퓨터 방송 — 좋은 아침입니다. 오늘은 항해 3일째입니다. 식당 불은 꺼졌고 다친 분은 의무실에서 쉬고 있습니다.";
        f.BroadcastAlpha = 1; f.Replaying = true; f.Replay = "식당 불 · 컷 2/5 정점"; f.Show = Overlay.Air; f.Legend = "공기";
        f.Captions = new[] { new ShipView.Audio.SoundCaption { Text = "쾅", Dir = "←", Count = 1, Left = 2 }, new ShipView.Audio.SoundCaption { Text = "경보", Dir = "", Count = 1, Left = 2 } };
        f.Story = "[불 · 화학 → 사람] 침대 밑에서 불씨가 튀었다"; f.StoryIsChain = true;
        return f;
    }

    private static void HudRules()
    {
        if (!Do("마우스")) return;
        Console.WriteLine("\n[마우스만으로 되는 화면 — 자리표 · 누름 · 뒤로 · 판 열쇠 · 출항 준비 · 확대 · 올림 · 넘기기]");
        var hud = new Hud();
        // 1 · 2: 겹침 0 · 화면 밖 0 · 누를 자리 크기 (여섯 창 × 사람 3 · 12 · 40 · 100 × 모두 켬 · 끔)
        {
            var bad = new List<string>(); var small = new List<string>(); int layouts = 0;
            foreach (var (pw, ph) in new[] { (1920, 1080), (1920, 1010), (1600, 900), (1366, 768), (1280, 720), (2560, 1440) })
                foreach (int n in new[] { 3, 12, 40, 100 })
                    foreach (int mode in new[] { 0, 1, 2 })
                    {
                        bool on = mode < 2;
                        var f = FakeFrame(n, on, mode == 0);
                        var st = new HudState();
                        HudLayout.Build(f, st, pw, ph, Hud.Approx, Hud.ApproxLines, hud);
                        layouts++;
                        foreach (var o in hud.Overlaps()) bad.Add($"{pw}×{ph} 사람 {n} {(mode == 0 ? "켬" : mode == 1 ? "말 창" : "끔")}: {o}");
                        foreach (var o in hud.Outside()) bad.Add($"{pw}×{ph} 사람 {n} {(mode == 0 ? "켬" : mode == 1 ? "말 창" : "끔")}: 밖 {o}");
                        foreach (var h in hud.Hits)
                        {
                            float need = h.Act is UiAct.Crew ? 30 : h.Act is UiAct.Robot or UiAct.RobotMore ? 26 : h.Act is UiAct.Pause or UiAct.Speed or UiAct.FixedMenu or UiAct.Sound or UiAct.Captions or UiAct.Help or UiAct.Menu or UiAct.ReplayStop ? 32 : h.Act is UiAct.Story ? 0 : 26;
                            if (h.R.H + 0.01f < need || h.R.W + 0.01f < (h.Act == UiAct.PanelBar ? 18 : Math.Min(need, 28))) small.Add($"{pw}×{ph} 사람 {n}: {h.Act} {h.R.W:0}×{h.R.H:0}");
                        }
                    }
            Check("마우스 자리표: 여섯 창 (1920×1080 · 최대화 1920×1010 · 1600×900 · 1366×768 · 1280×720 · 2560×1440) × 사람 3 · 12 · 40 · 100 (로봇 ⅓) × 모두 켬 (판 / 말 창) · 끔 — 가리는 판끼리 겹침 0 · 화면 밖 0 · 누를 자리 높이 (사람 줄 30 · 주 단추 32 · 그 밖 26 이상)",
                bad.Count == 0 && small.Count == 0, $"자리표 {layouts}가지 · 겹침 · 밖 {bad.Count} {string.Join(" | ", bad.Take(4))} · 작은 자리 {small.Count} {string.Join(" | ", small.Take(3))}");
        }
        // 3: 누름이 새지 않는다 · 같은 손잡이일 때만 · 자막 위는 세계로
        {
            var f = FakeFrame(12, true); var st = new HudState();
            HudLayout.Build(f, st, 1920, 1080, Hud.Approx, Hud.ApproxLines, hud);
            var card = hud.Area("카드"); var crew = hud.Area("사람 줄"); var tab0 = hud.Area("탭0"); var tab1 = hud.Area("탭1");
            var r1 = hud.At(card.X + 60, card.Bottom - 10);                       // 카드 몸
            var r2 = hud.At(tab0.Right + 3, tab0.Y + 10);                         // 탭 사이
            var r3 = hud.At(400, 20);                                             // 위 줄 빈 곳 (상태 글)
            var slots = hud.CrewSlots;
            var r4 = slots.Count > 1 ? hud.At(slots[0].R.Right + 2, slots[0].R.Y + 10) : default;   // 사람 줄 칸 사이
            var cap = hud.Area("자막");
            var r5 = hud.At(cap.X + 10, cap.Y + 10);                              // 소리 자막
            // 같은 손잡이: 탭 0에서 눌러 탭 1에서 놓으면 아무 일 없음 · 같은 탭에서 놓으면 그 탭
            st.Press(hud.At(tab0.X + 5, tab0.Y + 5)); var other = st.Release(hud.At(tab1.X + 5, tab1.Y + 5));
            st.Press(hud.At(tab0.X + 5, tab0.Y + 5)); var same = st.Release(hud.At(tab0.X + 6, tab0.Y + 6));
            bool ok = r1.Kind == RouteKind.Blocked && r2.Kind == RouteKind.Blocked && r3.Kind == RouteKind.Blocked && r4.Kind == RouteKind.Blocked && r5.Kind == RouteKind.World && other == null && same is { Act: UiAct.Tab, Key: 0 };
            Check("마우스 누름: 카드 몸 · 탭 사이 · 위 줄 빈 곳 · 사람 줄 칸 사이는 세계로 새지 않는다 (가리는 판) · 소리 자막 위는 세계로 · 눌러 놓은 손잡이가 같을 때만 실행",
                ok, $"카드 {r1.Kind} · 탭 사이 {r2.Kind} · 위 줄 {r3.Kind} · 칸 사이 {r4.Kind} · 자막 {r5.Kind} · 다른 탭에서 놓음 {(other == null ? "없음" : other.Act.ToString())} · 같은 탭 {same?.Act}");
        }
        // 4: 오른쪽 누르기는 뒤로 — 확인 창 → 펼친 목록 → 도움말 → 판 → 고른 이 → 없음 (다시보기는 끝내지 않는다)
        {
            var f = FakeFrame(3, true); var st = new HudState { Ask = HudAsk.Restart, Open = HudOpen.Menu, Help = true };
            var steps = new List<BackStep>();
            for (int i = 0; i < 6; i++)
            {
                var b = st.Back(f);
                steps.Add(b);
                if (b == BackStep.ClosePanel) f.Panel = PanelKind.None;
                if (b == BackStep.Release) f.Selected = -1;
            }
            var want = new[] { BackStep.CloseAsk, BackStep.CloseOpen, BackStep.CloseHelp, BackStep.ClosePanel, BackStep.Release, BackStep.None };
            Check("마우스 뒤로: 오른쪽 누르기 여섯 번 — 확인 창 → 펼친 목록 → 도움말 → 판 → 고른 이 → 없음 (다시보기는 끝내지 않는다 — 다시보기 띠의 ■ · B)",
                steps.SequenceEqual(want) && f.Replaying, string.Join(" → ", steps));
        }
        // 14: 분수 휠 셋 (0.4 × 3)이면 한 줄 · 끝을 넘지 않는다
        {
            var st = new HudState();
            st.Scroll(0.4, 30, 8); st.Scroll(0.4, 30, 8); int afterTwo = st.PanelFirst; st.Scroll(0.4, 30, 8); int afterThree = st.PanelFirst;
            for (int i = 0; i < 100; i++) st.Scroll(1, 30, 8);
            Check("마우스 넘기기: 분수 휠은 모아서 한 줄이 되면 넘긴다 (0.4 × 3 → 한 줄) · 끝을 넘지 않는다", afterTwo == 0 && afterThree == 1 && st.PanelFirst == 22, $"두 번 {afterTwo} · 세 번 {afterThree} · 끝 {st.PanelFirst}");
        }
        // 15: 화면 글자 — 풀이 글 · 판 바닥 글에 글꼴에 없는 글자(✕ ▾ ✔ ≡)가 없다 (도형으로 그린다)
        {
            var f = FakeFrame(12, true); var st = new HudState { Open = HudOpen.Menu };
            HudLayout.Build(f, st, 1920, 1080, Hud.Approx, Hud.ApproxLines, hud);
            var texts = hud.Hits.Select(h => h.Tip + h.Why).Concat(HudLayout.TabNames).Concat(LiveShow.SpeedLabels).Concat(HudLayout.OverlayWords).ToList();
            var bad = texts.Where(t => t.Any(c => Hud.Missing.Contains(c))).ToList();
            Check("마우스 글자: 풀이 글 · 탭 · 빠르기 · 겹쳐 보기 글에 글꼴(Pretendard)에 없는 글자(✕ ▾ ✔ ≡)가 없다 — 그것들은 도형으로 그린다", bad.Count == 0, bad.Count == 0 ? $"글 {texts.Count}줄" : string.Join(" | ", bad.Take(3)));
        }

        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        // 5: 판 줄 열쇠 — 고른 줄은 열쇠로: 사람 판 위험 순서가 바뀌어도 같은 사람 · 장면 판에 새 하이라이트가 끼어도 ▶가 원래 장면 · 주컴퓨터 판에 사건이 끼어도 펼친 일지가 같다
        {
            var s = Worlds.Full(lay, 61);
            var m = s.Get<Mind>();
            int a = m.AddPerson(s, At(3, 2)), b = m.AddPerson(s, At(20, 2));
            s.Run(3);
            var show = new LiveShow(s, "시험호") { FrameBudget = 1000 };
            show.TogglePanel(PanelKind.People);
            var f0 = show.Render(); show.Recycle(f0.Img);
            long keyB = LiveShow.KeyPerson(b);
            show.PanelClick(keyB);   // 사람 판: 누르면 따라간다 (판을 닫는다)
            bool followedB = show.Selected == b && show.Panel == PanelKind.None;
            show.Release();
            show.TogglePanel(PanelKind.People);
            show.PanelPickKey = keyB;
            s.Get<Life>()[m[a].C.Body].Conscious = 0;   // 위험 순서가 바뀐다 (쓰러진 이가 맨 위)
            var f1 = show.Render(); show.Recycle(f1.Img);
            bool pickStill = f1.PanelPick >= 0 && f1.PanelRows[f1.PanelPick].Key == keyB;
            bool enterB = show.PanelEnter() && show.Selected == b;
            Check("마우스 판 열쇠: 사람 판 줄을 누르면 그 사람을 따라간다 · 위험 순서가 바뀌어도 고른 줄 · Enter가 같은 사람 (예전엔 차례 번호라 다른 사람)",
                followedB && pickStill && enterB, $"누름 {followedB} · 순서가 바뀐 뒤 고른 줄 {pickStill} · Enter {enterB}");
        }
        // 6: 출항 준비 — 멈춘 채 항로 판 · 항로를 누르면 고르기만 (아직 멈춤) · 빠르기 단추로 떠난다 · 떠난 뒤엔 못 고른다
        {
            var show = LiveShow.Crewed("Didim", 18);
            show.FrameBudget = 1000;
            show.HoldForDeparture();
            long t0 = show.Sim.Tick;
            for (int i = 0; i < 30; i++) show.Advance(1 / 60.0);
            bool still = show.Sim.Tick == t0 && show.Holding && show.Panel == PanelKind.Route;
            bool picked = show.PanelClick(8) && show.Sim.Get<Outside>().Route == 8 && show.Holding;
            show.SetSpeed(2);
            bool gone = !show.Holding && !show.Paused && show.Panel == PanelKind.None;
            double left = show.DepartInMin();
            for (int k = 0; k < 400 && !double.IsNaN(show.DepartInMin()); k++)
            {
                show.FastForward(25);
            }
            show.TogglePanel(PanelKind.Route);
            bool clicked = show.PanelClick(2);
            bool locked = !clicked && show.Notice.Contains("이미 떠났다");
            Check("마우스 출항 준비: 띄우면 세계가 멈춘 채 항로 판 (틱 그대로) · 항로를 누르면 고르기만 (아직 멈춤) · 빠르기 단추로 떠난다 · 출발 점화가 시작된 뒤엔 항로를 못 고르고 까닭을 알린다 (예전엔 점화 중에도 받아 항해를 다시 짰다)",
                still && picked && gone && locked, $"멈춤 {still} · 항로 8 고름 {picked} · 떠남 {gone} (출발까지 {left:0}분) · 떠난 뒤 {locked}");
        }
        // 8 · 13: 커서 쪽 확대 (그 칸이 화면의 같은 자리에 남는다) · 올림과 누름이 같은 사람
        {
            var s = Worlds.Full(lay, 62);
            var m = s.Get<Mind>();
            int p = m.AddPerson(s, At(15, 3));
            s.Run(3);
            var show = new LiveShow(s, "시험호") { FrameBudget = 1000 };
            show.Follow = false; show.CamX = 12; show.CamY = 5; show.ViewW = 24;
            var (l, t, w, h) = show.Camera();
            double cx = l + w * 0.3, cy = t + h * 0.6;
            show.ZoomAt(0.7, cx, cy);
            var (l2, t2, w2, h2) = show.Camera();
            double drift = Math.Max(Math.Abs((cx - l2) / w2 - 0.3) * w2, Math.Abs((cy - t2) / h2 - 0.6) * h2);
            var spots = show.SpotsNow();
            var sp = spots.First(x => x.Id == p);
            int byNear = LiveShow.Nearest(spots, sp.X, sp.Y), byNear2 = LiveShow.Nearest(spots, sp.X + 0.3, sp.Y - 0.2), none = LiveShow.Nearest(spots, sp.X + 5, sp.Y);
            bool sel = show.SelectAt(sp.X + 0.3, sp.Y - 0.2) && show.Selected == byNear2;
            show.SetHover(p);
            var hf = show.Render(); show.Recycle(hf.Img);
            Check("마우스 확대 · 올림: 커서 쪽 확대는 그 칸을 화면의 같은 자리에 남긴다 (0.05칸 안) · 올린 사람(Nearest)과 누른 사람(SelectAt)이 같다 · 올린 한 명의 이름만 실린다",
                drift < 0.05 && w2 < w && byNear == p && byNear2 == p && none == -1 && sel && hf.HoverName == m.NameOf(p),
                $"어긋남 {drift:0.000}칸 · 너비 {w:0.0} → {w2:0.0} · 올림 {byNear}/{byNear2}/{none} · 누름 {sel} · 올린 이 '{hf.HoverName}'");
        }
    }
}
