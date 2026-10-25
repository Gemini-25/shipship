using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>마우스만으로 되는 화면 (docs/UI.md 8 — 함장 요청 10-23): 자리표 · 누름 · 뒤로 · 판 줄 열쇠 · 출항 준비 · 커서 쪽 확대 · 올림과 누름 · 넘기기 — 세계는 그대로.</summary>
public static partial class Program
{
    /// <summary>자리표 시험용 그림 한 장 (사람 n · 로봇 n/3 · 모두 켬 / 모두 끔 · 판을 닫으면 말 창 · 판 갈래 · 자세히 — docs/UI2.md §8 M1).</summary>
    private static LiveFrame FakeFrame(int people, bool on, bool panel = true, PanelKind kind = PanelKind.People, DetailKind detail = DetailKind.None)
    {
        var f = new LiveFrame
        {
            Status = "다시보기 · 한누리호 · 3일 14:22 · 결정적 순간 · ×120 · 하루 6분 · 사람 100/100 · 따라 보기 (천천히)",
            Vitals = new[]
            {
                new Gauge("air", "공기", "산소 20.8kPa · 이산화탄소 0.6kPa · 일산화탄소 30ppm", 1, "산소 20.8"), new Gauge("crew", "사람", "100/100 · 쓰러짐 12 · 다침 34", 2, "쓰러짐 12"),
                new Gauge("power", "전력", "182/240kW", 0, "182/240"), new Gauge("hull", "선체", "구멍 둘 · 빠지는 방 하나", 2, "구멍 둘"), new Gauge("supply", "물자", "식량 넉넉 · 물 넉넉", 0, "넉넉"), new Gauge("fire", "불", "셋 · 주컴퓨터는 아직 모른다", 2, "셋 · 모른다"),
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
        if (kind != PanelKind.People || detail != DetailKind.None)
        {
            f.Panel = kind;
            f.PanelTitle = kind switch { PanelKind.Robots => "로봇 · 드론", PanelKind.Hand => "사고 일으키기", PanelKind.Journal => "주컴퓨터", _ => "사람" };
            if (kind == PanelKind.Hand) f.PanelRows = new[] { new PanelRow("곧 — 불씨 · 외판 구멍 · 고장 · 운석 …", "줄을 누르면 겨눈다 · 배 위를 누르면 일어난다", false, Key: -1) };
            f.Detail = detail;
            if (detail != DetailKind.None)
            {
                f.DetailTabs = detail switch { DetailKind.Person => LiveShow.PersonTabs, DetailKind.Robot => LiveShow.RobotTabs, _ => LiveShow.ComputerTabs };
                f.DetailId = detail == DetailKind.Computer ? -1 : 0; f.DetailSub = 1; f.DetailFace = detail == DetailKind.Person ? 0 : -1; f.DetailFollow = detail != DetailKind.Computer;
                if (detail != DetailKind.Computer) f.PanelTitle = detail == DetailKind.Person ? "김하늘 '불꽃' · 기관사" : "로봇 1";
                f.DetailLine = detail == DetailKind.Computer ? "켜짐 · 센서 12초마다 · 빨아들이는 공기 31℃ · 붙잡은 사건 2" : "식당 · 불을 끈다 ▸ 소화기를 가지러 · 3분째";
                f.PanelFoot = "줄을 누르면 펼친다 · 휠로 넘기기 · 오른쪽 누르기로 목록 (← → · Esc)";
                f.PanelRows = Enumerable.Range(0, 30).Select(i => new PanelRow("숨 · 피 " + i, "핏속 산소 95% · 일산화탄소 0%", false, Bars: new[] { new PanelBar("산소", 0.9f, 0) }, Key: LiveShow.KeyDetail(1, i))).ToArray();
                f.PanelPickKey = f.PanelRows[0].Key;
            }
        }
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
                    foreach (int mode in new[] { 0, 1, 2, 3, 4, 5, 6, 7 })
                    {
                        bool on = mode != 2;
                        var (kind, detail) = mode switch { 3 => (PanelKind.People, DetailKind.Person), 4 => (PanelKind.Robots, DetailKind.Robot), 5 => (PanelKind.Journal, DetailKind.Computer), 6 => (PanelKind.Hand, DetailKind.None), 7 => (PanelKind.Robots, DetailKind.None), _ => (PanelKind.People, DetailKind.None) };
                        var f = FakeFrame(n, on, mode != 1, kind, detail);
                        var st = new HudState();
                        HudLayout.Build(f, st, pw, ph, Hud.Approx, Hud.ApproxLines, hud);
                        layouts++;
                        string tag = mode switch { 0 => "켬", 1 => "말 창", 2 => "끔", 3 => "사람 자세히", 4 => "로봇 자세히", 5 => "주컴퓨터", 6 => "사고 판", _ => "로봇 판" };
                        foreach (var o in hud.Overlaps()) bad.Add($"{pw}×{ph} 사람 {n} {tag}: {o}");
                        foreach (var o in hud.Outside()) bad.Add($"{pw}×{ph} 사람 {n} {tag}: 밖 {o}");
                        // 탭 일곱 · 자세한 판 (넓은 폭 · 작은 탭 한 줄 · 판 안 · [목록] · [따라가기]은 사람 · 로봇만) · 카드 [자세히 보기]
                        int tabs = hud.Hits.Count(x => x.Act == UiAct.Tab);
                        if (tabs != 7) bad.Add($"{pw}×{ph} {tag}: 탭 {tabs}");
                        if (on && f.Card.Length > 0 && !hud.Hits.Any(x => x.Act == UiAct.DetailOpen)) bad.Add($"{pw}×{ph} {tag}: 카드 [자세히 보기] 없음");
                        if (detail != DetailKind.None)
                        {
                            var box = hud.Area("판");
                            float pwd = Math.Clamp(hud.W - 32 - HudLayout.LeftCol - 300, 560, 920);
                            if (Math.Abs(box.X - (hud.W - HudLayout.Side - pwd)) > 0.5f) bad.Add($"{pw}×{ph} {tag}: 판 왼쪽 {box.X:0} (넓은 폭 {pwd:0})");
                            var sub = hud.Hits.Where(x => x.Act == UiAct.DetailTab).ToList();
                            if (sub.Count != f.DetailTabs.Length || sub.Any(x => x.R.Y != sub[0].R.Y || x.R.Right > box.Right - 8 || x.R.X < box.X)) bad.Add($"{pw}×{ph} {tag}: 작은 탭 {sub.Count}/{f.DetailTabs.Length} · 한 줄 · 판 안 아님");
                            bool list = hud.Hits.Any(x => x.Act == UiAct.DetailBack), follow = hud.Hits.Any(x => x.Act == UiAct.DetailFollow);
                            if (list != (detail != DetailKind.Computer) || follow != (detail != DetailKind.Computer)) bad.Add($"{pw}×{ph} {tag}: [목록] {list} · [따라가기] {follow}");
                            if (hud.PanelLines.Count > 0 && hud.PanelLines[0].R.Y < box.Y + 92 - 0.5f) bad.Add($"{pw}×{ph} {tag}: 줄이 머리 · 작은 탭과 겹친다");
                        }
                        foreach (var h in hud.Hits)
                        {
                            float need = h.Act is UiAct.Crew ? 30 : h.Act is UiAct.Robot or UiAct.RobotMore ? 26 : h.Act is UiAct.Pause or UiAct.Speed or UiAct.FixedMenu or UiAct.Sound or UiAct.Captions or UiAct.Help or UiAct.Menu or UiAct.ReplayStop ? 32 : h.Act is UiAct.Story ? 0 : 26;
                            if (h.R.H + 0.01f < need || h.R.W + 0.01f < (h.Act == UiAct.PanelBar ? 18 : Math.Min(need, 28))) small.Add($"{pw}×{ph} 사람 {n}: {h.Act} {h.R.W:0}×{h.R.H:0}");
                        }
                    }
            Check("마우스 자리표: 여섯 창 (1920×1080 · 최대화 1920×1010 · 1600×900 · 1366×768 · 1280×720 · 2560×1440) × 사람 3 · 12 · 40 · 100 (로봇 ⅓) × 모두 켬 (판 / 말 창) · 끔 · 자세한 판 (사람 · 로봇 · 주컴퓨터) · 사고 판 · 로봇 판 — 가리는 판끼리 겹침 0 · 화면 밖 0 · 누를 자리 높이 (사람 줄 30 · 주 단추 32 · 그 밖 26 이상) · 탭 일곱 · 자세한 판은 넓은 폭 · 작은 탭 한 줄 (docs/UI2.md M1)",
                bad.Count == 0 && small.Count == 0, $"자리표 {layouts}가지 · 겹침 · 밖 {bad.Count} {string.Join(" | ", bad.Take(4))} · 작은 자리 {small.Count} {string.Join(" | ", small.Take(3))}");
        }
        // 3: 누름이 새지 않는다 · 같은 손잡이일 때만 · 자막 위는 세계로
        {
            var f = FakeFrame(12, true); var st = new HudState();
            HudLayout.Build(f, st, 1920, 1080, Hud.Approx, Hud.ApproxLines, hud);
            var card = hud.Area("카드"); var crew = hud.Area("사람 줄"); var tab0 = hud.Area("탭0"); var tab1 = hud.Area("탭1");
            var r1 = hud.At(card.X + 60, card.Y + card.H / 2);                   // 카드 몸 (맨 아래는 [자세히 보기])
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
        // 4 (M2): 오른쪽 누르기는 뒤로 — 확인 창 → 펼친 목록 → 도움말 → 자세히 (목록으로) → 판 → 고른 이 → 없음 (다시보기는 끝내지 않는다) · 주컴퓨터는 목록이 없어 판을 닫는다
        {
            var f = FakeFrame(3, true, true, PanelKind.People, DetailKind.Person); var st = new HudState { Ask = HudAsk.Restart, Open = HudOpen.Menu, Help = true };
            var steps = new List<BackStep>();
            for (int i = 0; i < 7; i++)
            {
                var b = st.Back(f);
                steps.Add(b);
                if (b == BackStep.CloseDetail) f.Detail = DetailKind.None;
                if (b == BackStep.ClosePanel) f.Panel = PanelKind.None;
                if (b == BackStep.Release) f.Selected = -1;
            }
            var want = new[] { BackStep.CloseAsk, BackStep.CloseOpen, BackStep.CloseHelp, BackStep.CloseDetail, BackStep.ClosePanel, BackStep.Release, BackStep.None };
            var fc = FakeFrame(3, true, true, PanelKind.Journal, DetailKind.Computer);
            var computer = new HudState().Back(fc);
            Check("마우스 뒤로: 오른쪽 누르기 일곱 번 — 확인 창 → 펼친 목록 → 도움말 → 자세히 (목록으로) → 판 → 고른 이 → 없음 (다시보기는 끝내지 않는다 — 다시보기 띠의 ■ · B) · 주컴퓨터 자세히는 목록이 없어 판을 닫는다 (docs/UI2.md M2)",
                steps.SequenceEqual(want) && f.Replaying && computer == BackStep.ClosePanel, string.Join(" → ", steps) + $" · 주컴퓨터 {computer}");
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
            var texts = new List<string>();
            foreach (var (kind, detail) in new[] { (PanelKind.People, DetailKind.None), (PanelKind.People, DetailKind.Person), (PanelKind.Robots, DetailKind.Robot), (PanelKind.Journal, DetailKind.Computer), (PanelKind.Hand, DetailKind.None) })
            {
                var f = FakeFrame(12, true, true, kind, detail); var st = new HudState { Open = HudOpen.Menu };
                HudLayout.Build(f, st, 1920, 1080, Hud.Approx, Hud.ApproxLines, hud);
                texts.AddRange(hud.Hits.Select(h => h.Tip + h.Why));
            }
            texts.AddRange(HudLayout.TabNames.Concat(LiveShow.SpeedLabels).Concat(HudLayout.OverlayWords).Concat(LiveShow.PersonTabs).Concat(LiveShow.RobotTabs).Concat(LiveShow.ComputerTabs));
            var bad = texts.Where(t => t.Any(c => Hud.Missing.Contains(c))).ToList();
            Check("마우스 글자: 풀이 글 · 탭 일곱 · 작은 탭 · 자세한 판 단추 · 빠르기 · 겹쳐 보기 글에 글꼴(Pretendard)에 없는 글자(✕ ▾ ✔ ≡)가 없다 — 그것들은 도형으로 그린다 (docs/UI2.md M4)", bad.Count == 0, bad.Count == 0 ? $"글 {texts.Count}줄" : string.Join(" | ", bad.Take(3)));
        }

        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        // 5 (M3 — 바뀜: 예전엔 사람 줄을 누르면 따라가며 판이 닫혀 자세히 볼 길이 없었다 · docs/UI2.md §7 결함 3): 판 줄 열쇠 — 사람 줄을 누르면 자세히 + 따라감 (판은 열린 채) ·
        //    목록으로 돌아오면 그 사람 줄 · 위험 순서가 바뀌어도 같은 사람 · Enter는 예전대로 따라가기 + 닫기
        {
            var s = Worlds.Full(lay, 61);
            var m = s.Get<Mind>();
            int a = m.AddPerson(s, At(3, 2)), b = m.AddPerson(s, At(20, 2));
            s.Run(3);
            var show = new LiveShow(s, "시험호") { FrameBudget = 1000 };
            show.TogglePanel(PanelKind.People);
            var f0 = show.Render(); show.Recycle(f0.Img);
            long keyB = LiveShow.KeyPerson(b);
            show.PanelClick(keyB);   // 사람 판: 누르면 자세히 + 따라간다 (판은 열린 채)
            var fd = show.Render(); show.Recycle(fd.Img);
            bool detailB = show.Selected == b && show.FollowSelected && show.Panel == PanelKind.People && fd.Detail == DetailKind.Person && fd.DetailId == b
                && fd.DetailTabs.Length == 6 && fd.PanelTitle.StartsWith(m.NameOf(b)) && fd.DetailFollow;
            show.DetailBack();
            show.Release();
            var fl = show.Render(); show.Recycle(fl.Img);
            bool backList = fl.Detail == DetailKind.None && fl.Panel == PanelKind.People && fl.PanelPick >= 0 && fl.PanelRows[fl.PanelPick].Key == keyB;
            s.Get<Life>()[m[a].C.Body].Conscious = 0;   // 위험 순서가 바뀐다 (쓰러진 이가 맨 위)
            var f1 = show.Render(); show.Recycle(f1.Img);
            bool pickStill = f1.PanelPick >= 0 && f1.PanelRows[f1.PanelPick].Key == keyB;
            bool enterB = show.PanelEnter() && show.Selected == b && show.Panel == PanelKind.None;
            Check("마우스 판 열쇠 (바뀜 — 결함 3): 사람 줄을 누르면 그 사람 자세히 + 따라감 · 판은 열린 채 (예전엔 판이 닫혀 자세히 볼 길이 없었다) · 목록으로 돌아오면 그 사람 줄 · 위험 순서가 바뀌어도 고른 줄 · Enter는 예전대로 따라가기 + 닫기 (docs/UI2.md M3)",
                detailB && backList && pickStill && enterB, $"자세히 + 따라감 {detailB} · 목록으로 {backList} · 순서가 바뀐 뒤 고른 줄 {pickStill} · Enter {enterB}");
        }
        // 자세한 판 · 로봇 판 (결함 4 — 로봇 · 드론이 사람 판 끝에 섞였다): 사람 판은 사람만 · 로봇 판 머리 줄 · 막대 넷 · 로봇 줄을 누르면 자세히 (작은 탭 넷) ·
        //    ← → 는 작은 탭 (첫 탭에서 ← = 목록) · 목록에서 → = 자세히 · 주컴퓨터 판은 열면 곧 자세히 '지금' (옛 J 판 줄 그대로) · 세계는 그대로
        {
            var s = Worlds.Full(lay, 63);
            var m = s.Get<Mind>();
            int p = m.AddPerson(s, At(3, 2)); m.AddPerson(s, At(20, 2));
            int r = m.AddRobot(s, At(20, 7), AgentKind.Robot, At(20, 7));
            s.Run(3);
            var before = s.Save();
            var show = new LiveShow(s, "시험호") { FrameBudget = 1000 };
            show.TogglePanel(PanelKind.People);
            bool peopleOnly = show.PanelRows().Where(x => x.Key >= LiveShow.KeyPerson(0)).All(x => m[(int)(x.Key - LiveShow.KeyPerson(0))].IsPerson) && !show.PanelRows().Any(x => x.Main.StartsWith(m.NameOf(r)));
            show.TogglePanel(PanelKind.Robots);
            var rf = show.Render(); show.Recycle(rf.Img);
            var rrow = rf.PanelRows.FirstOrDefault(x => x.Key == LiveShow.KeyPerson(r));
            bool robotsPanel = rf.Panel == PanelKind.Robots && rf.PanelRows.Length >= 2 && rf.PanelRows[0].Key < 0 && rf.PanelRows[0].Main.Contains("로봇 1")
                && rrow.Bars is { Length: 4 } && rrow.Bars.Select(x => x.Name).SequenceEqual(new[] { "전지", "다리", "팔", "눈" });
            show.PanelClick(LiveShow.KeyPerson(r));
            var rd = show.Render(); show.Recycle(rd.Img);
            bool robotDetail = rd.Detail == DetailKind.Robot && rd.DetailId == r && rd.DetailTabs.Length == 4 && show.Selected == r && rd.Panel == PanelKind.Robots && rd.DetailLine.Contains("전지");
            show.PanelSide(+1); int sub1 = show.DetailSub;
            show.PanelSide(-1); show.PanelSide(-1);
            bool sideBack = sub1 == 1 && show.Detail == DetailKind.None && show.Panel == PanelKind.Robots;
            show.PanelSide(+1);
            bool sideOpen = show.Detail == DetailKind.Robot && show.DetailId == r;
            show.OpenDetail(DetailKind.Person, r);   // 카드의 [자세히 보기]: 로봇이면 로봇 자세히로 고친다
            bool kindFix = show.Detail == DetailKind.Robot && show.Panel == PanelKind.Robots;
            var old = new LiveShow(s, "시험호"); old.TogglePanel(PanelKind.Journal);
            show.TogglePanel(PanelKind.Journal);
            var jf = show.Render(); show.Recycle(jf.Img);
            bool computer = jf.Detail == DetailKind.Computer && jf.DetailSub == 0 && jf.DetailTabs.Length == 6 && jf.PanelRows.Any(x => x.Main == "배 형편")
                && jf.PanelRows.Select(x => x.Main).SequenceEqual(old.PanelRows().Select(x => x.Main));
            show.PanelSide(+1); bool jsub = show.DetailSub == 1 && show.Panel == PanelKind.Journal;
            show.PanelSide(-1); show.PanelSide(-1); bool jstay = show.DetailSub == 0 && show.Panel == PanelKind.Journal && show.Detail == DetailKind.Computer;
            show.OpenDetail(DetailKind.Person, p); show.SetDetailSub(5);
            var pf = show.Render(); show.Recycle(pf.Img);
            bool personFace = pf.DetailFace >= 0 && pf.DetailSub == 5 && pf.PanelRows.Length > 0 && pf.DetailLine.Length > 0;
            bool same = s.Save().AsSpan().SequenceEqual(before);
            Check("마우스 자세한 판 · 로봇 판 (결함 4): 사람 판은 사람만 · 로봇 판 머리 줄 '로봇 1' · 줄마다 전지 · 다리 · 팔 · 눈 막대 · 로봇 줄을 누르면 자세히 (작은 탭 넷 · 따라감) · ← → 는 작은 탭 (첫 탭에서 ← = 목록) · 목록에서 → = 자세히 · 주컴퓨터 판은 열면 곧 자세히 '지금' (옛 J 판 줄 그대로 · 목록이 없어 ←로 닫히지 않는다) · 세계는 그대로",
                peopleOnly && robotsPanel && robotDetail && sideBack && sideOpen && kindFix && computer && jsub && jstay && personFace && same,
                $"사람만 {peopleOnly} · 로봇 판 {robotsPanel} ({string.Join(" / ", rf.PanelRows.Take(2).Select(x => x.Main))}) · 로봇 자세히 {robotDetail} · ← → {sideBack}/{sideOpen} · 갈래 고침 {kindFix} · 주컴퓨터 {computer} · 작은 탭 {jsub}/{jstay} · 사람 얼굴 {personFace} · 세계 그대로 {same}");
        }
        // 상태 띠 짧은 값 (결함 5 — UI.md §2에 적었으나 코드에 없었다): 탭이 일곱이 되어 1366×768에서 띠가 탭 왼쪽을 넘으면 칸마다 짧은 값 (자르지 않는다) · 넓은 창은 긴 값 그대로
        {
            var f = FakeFrame(100, false);
            var st = new HudState();
            HudLayout.Build(f, st, 1366, 768, Hud.Approx, Hud.ApproxLines, hud);
            bool shortOn = hud.VitalsShort;
            float Tw(string t, int size, bool bold = false) => Hud.Approx(t, Math.Max(13, (int)Math.Round(size * hud.U)), bold) / hud.U * 1.03f;
            int whole = 0;
            for (int i = 0; i < f.Vitals.Length; i++)
                if (hud.Areas.TryGetValue("띠" + i, out var r) && Math.Abs(r.W - (30 + Tw(f.Vitals[i].Name, 14, true) + 8 + Tw(f.Vitals[i].Short, 14) + 12)) < 0.5f) whole++;
            bool tipFull = hud.Hits.Any(x => x.Act == UiAct.Vital && x.Tip.Contains(f.Vitals[0].Value));
            HudLayout.Build(f, st, 3840, 1080, Hud.Approx, Hud.ApproxLines, hud);
            bool wideLong = !hud.VitalsShort;
            Check("마우스 상태 띠 짧은 값 (결함 5): 1366×768 · 탭 일곱이면 띠가 탭 왼쪽을 넘어 칸마다 짧은 값 (여섯 칸 모두 자르지 않고) · 전체 값은 풀이 글로 · 3840×1080 넓은 창은 긴 값 그대로",
                shortOn && whole == f.Vitals.Length && tipFull && wideLong, $"짧은 값 {shortOn} · 자르지 않은 칸 {whole}/{f.Vitals.Length} · 풀이 글 {tipFull} · 넓은 창 긴 값 {wideLong}");
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
