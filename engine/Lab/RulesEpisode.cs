using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>
/// 경과 (docs/OBSERVER.md §7.1 ~ 7.3 · §16 E1 ~ E3 · M5 — 묶음 '경과'): 뿌리마다 사건의 마디 (일어남 → 처음 안 이 → 주컴퓨터가 앎 → 맡김 → 닿음 → 끔 · 막음 · 고침 → 끝) ·
/// 늦은 까닭 (아는 것만 — 무선 센서 방 · 센서 선이 끊긴 방 · 맡은 이가 막힘) · 견줌 (같은 갈래 지난 경과 셋부터 평균) · 결산 띠 6초 · 사건 띠 줄 → 그 카드 · 손 결과 이름표 · 자리표.
/// 연출 장부라 세계를 바꾸지 않는다 (쌍둥이 바이트) · 저장 왕복.
/// </summary>
public static partial class Program
{
    private static void EpisodeRules()
    {
        if (!Do("경과")) return;
        Console.WriteLine("\n[경과 — 사건 경과 카드 · 결산 띠 · 손 결과 이름표]");
        static string Ms(long ticks) { long s = (long)Math.Round(ticks * Time.SecondsPerTick); return $"{s / 60}:{s % 60:00}"; }
        if (Environment.GetEnvironmentVariable("EPISODE_BENCH") == "1")
        {
            // 재기 (판정하지 않는다): 새뜰호 침대 불씨 (함장의 손) — 열린 경과가 있는 틱의 연출 몫 (경과만) · 카드 짓기
            var lay = Layout.Ship("Saetteul", Tables.Default);
            var w = ShipWorld.Crewed("Saetteul", 7, lay.Furniture.Count(f => f.Type == "Bed"), lay.Furniture.Count(f => f.Type == "RobotDock"), 0);
            var b = new LiveShow(w, lay.Name);
            int bed = lay.Furniture.First(f => f.Type == "Bed").Cells[0];
            w.Get<Hand>().Enqueue(Order(HandKind.Fire, bed, 2));
            b.FastForward(Time.Minutes(3));
            var ep = b.Dir.Episodes.LastOrDefault();
            var tick = typeof(Director).GetMethod("EpisodeTick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.CreateDelegate<Action<Sim, Mind?, long>>(b.Dir);
            var mind = w.Get<Mind>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 20_000; i++) tick(w, mind, w.Tick);
            double per = sw.Elapsed.TotalMilliseconds * 1000 / 20_000;
            if (ep != null) b.OpenEpisode(ep.Root);
            b.Card();
            sw.Restart();
            for (int i = 0; i < 2000; i++) b.Card();
            double card = sw.Elapsed.TotalMilliseconds / 2000;
            Console.WriteLine($"    (경과 재기) 새뜰호 열린 경과 {b.Dir.Episodes.Count(e => e.End < 0)}: 틱마다 {per:0.00}µs · 카드 짓기 {card:0.000}ms");
        }

        // E1: 불씨 손 (디딤 · 로봇 2) → 마디 차례 (일어남 ≤ 처음 안 이 · 주컴퓨터 ≤ 맡김 ≤ 닿음 ≤ 끔 ≤ 끝) · 머리 '함장이 일으킨 불씨' · ▶ 다시보기 · 손 카메라 뒤에도 그 카드 · 세계 바이트는 쌍둥이와 같다 · 저장 왕복
        Episode? fire = null; LiveShow? fshow = null;
        {
            var show = new LiveShow(HandShip(121), "디딤호") { FrameBudget = 1000 };
            var twin = HandShip(121);
            var s = show.Sim; var m = s.Get<Mind>(); var aim = HandTargets(s);
            show.FastForward(Time.Minutes(2) + 3); twin.Run((int)(Time.Minutes(2) + 3));
            show.ArmHand(HandKind.Fire, 2);   // 큰 불씨 (저절로 꺼지지 않는다 — 맡은 이가 닿아 끈다)
            var L = s.Layout;
            show.HandAt(L.X(aim.Bed) + 0.5, L.Y(aim.Bed) + 0.5);   // 받아 둔다 → 다음 틱에 일어난다 (손 카메라)
            twin.Get<Hand>().Enqueue(Order(HandKind.Fire, aim.Bed, 2));
            show.FastForward(1); twin.Run(1);
            bool cardNow = show.Card().Length > 0 && show.Card()[0].StartsWith("함장이 일으킨 불씨");   // 손을 쓰면 그 경과 카드가 곧 열린다
            show.SetFixed(4);   // 빨리 감는 빠르기 (한 장에 50틱 — 미리 보기는 쉰다) · 화면 길로 나아가야 끝날 때 결산 띠가 선다 (감기는 띠를 띄우지 않는다)
            var thinker = s.Get<ShipMind>();
            for (int i = 0; i < 40 && (show.Dir.Episodes.LastOrDefault()?.End ?? 0) < 0; i++) { thinker.WaitThoughts(); long t0 = s.Tick; show.Advance(0.2); twin.Run((int)(s.Tick - t0)); }   // 주컴퓨터 일꾼의 셈을 받아 둔 뒤 (시험은 화면보다 빨라 일꾼을 기다린 장이 서 있다)
            var e = show.Dir.Episodes.LastOrDefault();
            fire = e; fshow = show;
            bool same = s.Save().AsSpan().SequenceEqual(twin.Save());
            // (디딤호 출항 2분 — 맡은 로봇이 소화기를 가지러 간 선반에서 먼저 집어 간 이가 있어 '쓸 물건이 없다'로 막히고 침대 불은 그새 저절로 꺼진다: 닿음은 있으면 맡김 뒤 · 끔은 맡은 이가 아니어도 된다)
            bool order = e != null && e.Hand && e.Kind == FactKind.Ignition && e.NoticeAt >= e.Start && e.ComputerAt >= e.Start && e.AssignAt >= e.ComputerAt && e.FixAt >= e.Start
                && (e.ArriveAt < 0 || e.ArriveAt >= e.AssignAt) && e.End >= Math.Max(e.FixAt, Math.Max(e.ArriveAt, e.AssignAt));
            string[] card = Array.Empty<string>();
            bool lines = false, replay = false;
            if (e != null)
            {
                show.OpenEpisode(e.Root);
                card = show.Card();
                string Line(string w) => card.FirstOrDefault(l => l.Contains(w)) ?? "";
                lines = card[0].StartsWith("함장이 일으킨 불씨 — ") && card[1].StartsWith("1일째 ") && card[1].Contains(" — 끝 ") && card[1].EndsWith("분")
                    && Line("일어남").StartsWith("0:00 ") && Line("처음 안 이").StartsWith(Ms(e.NoticeAt - e.Start) + " ") && Line("처음 안 이").Contains(m.NameOf(e.Noticer))
                    && Line("주컴퓨터가 앎").StartsWith(Ms(e.ComputerAt - e.Start) + " ") && Line("맡김").Contains(m.NameOf(e.Assignee)) && (e.ArriveAt < 0 || Line("닿음").StartsWith(Ms(e.ArriveAt - e.Start) + " "))
                    && Line(e.Fixer >= 0 ? "끔 — " : "꺼짐 — ").StartsWith(Ms(e.FixAt - e.Start) + " ") && Line("아는 이").Length > 0;
                var f = show.Render();
                replay = f.CardIs == CardKind.Episode && f.EpisodeDots.Length >= 6 && f.EpisodeDots.Zip(f.EpisodeDots.Skip(1)).All(p => p.First.At <= p.Second.At) && (show.Dir.Highlights.Any(h => h.Root == e.Root) ? f.EpisodeCanReplay : true);
            }
            // 저장 왕복: 연출 기록에 경과가 실려 되살린 연출이 같은 경과를 안다
            var rw = new StateWriter(1 << 16); show.Dir.SaveRecord(rw);
            var back = new Director(s.Layout);
            bool loaded = back.LoadRecord(new StateReader(rw.ToArray()), s);
            var rw2 = new StateWriter(1 << 16); back.SaveRecord(rw2);
            bool round = loaded && back.Episodes.Count == show.Dir.Episodes.Count && rw.ToArray().AsSpan().SequenceEqual(rw2.ToArray()) && back.Episodes.LastOrDefault()?.Title == e?.Title;
            Check("경과 E1: 불씨 손 (디딤 · 로봇 2) → 경과 하나 — 일어남 ≤ 처음 안 이 · 주컴퓨터가 앎 ≤ 맡김 (≤ 닿음) · 끔 · 꺼짐 ≤ 끝 · 카드 머리 '함장이 일으킨 불씨 — …' (손을 쓰면 곧 열린다) · 마디마다 'm:ss' · 가로 시계 점 차례 · ▶ 다시보기 / 세계 바이트는 쌍둥이와 같다 · 연출 기록 왕복",
                e != null && order && cardNow && lines && replay && same && round,
                e == null ? "경과가 없다" : $"차례 {order} (일어남 0 · 앎 {e.NoticeAt - e.Start} · 주컴퓨터 {e.ComputerAt - e.Start} · 맡김 {e.AssignAt - e.Start} · 닿음 {e.ArriveAt - e.Start} · 끔 {e.FixAt - e.Start} · 끝 {e.End - e.Start}틱) · 곧 열림 {cardNow} · 줄 {lines} · 점 {replay} · 쌍둥이 {same} · 왕복 {round} [{string.Join(" | ", card)}]");
        }

        // E2: 늦은 까닭 (아는 것만) — 무선 센서 방 '무선 센서다 (36초마다 읽는다)' · 센서 선이 끊겨 못 보는 방 · 맡은 이가 막혔다 (까닭 글)
        {
            var bad = new List<string>(); string seen = "";
            string? Notes(Func<Sim, int> pickRoom, Action<Sim>? before, string want)
            {
                var show = new LiveShow(HandShip(122), "디딤호") { FrameBudget = 1000 };
                var s = show.Sim; var L = s.Layout; var aim = HandTargets(s);
                before?.Invoke(s);
                show.FastForward(5);
                int room = pickRoom(s);
                if (room < 0) return null;
                int cell = L.Rooms[room].Cells.First(c => L.IsSpace(c) && L.FurnitureAt[c] < 0);
                s.Facts.Emit(s.Tick, FactKind.Ignition, cell, 0, s.Causes.Add(CauseKind.Setup, "시험 — 불씨", s.Tick, cell));
                show.FastForward(2);
                var e = show.Dir.Episodes.LastOrDefault();
                string why = e == null ? "" : string.Join(" · ", e.Why);
                seen += $" [{(e == null ? "없음" : L.Rooms[room].Name + ": " + why)}]";
                return why.Contains(want) ? why : null;
            }
            // 무선 방: 센서 선을 끊으면 (함장의 손) 무선으로만 닿는 방
            string? radio = Notes(s => { var sm = s.Get<ShipMind>(); for (int r = 0; r < sm.RoomSeen.Length; r++) if (sm.RoomSeen[r] == 2) return r; return -1; },
                s =>
                {
                    // 침대 방의 센서 선 가운데를 끊는다 (규칙 '센서 선이 끊겨도 전기가 있으면 무선으로 본다'와 같은 차림 — 옆 방을 거쳐 닿는다)
                    var aim = HandTargets(s); var net = s.Get<Network>(); var p = net.DataPath[s.Layout.Room[aim.Bed]];
                    net.CutData(s, p[p.Length / 2], s.Causes.Add(CauseKind.Setup, "시험 — 센서 선이 끊겼다", s.Tick, p[p.Length / 2]));
                    s.Run(3);
                }, "무선 센서다 (36초마다 읽는다)");
            if (radio == null) bad.Add("무선 방 까닭 없음");
            // 못 보는 방: 그 방 전기를 끊으면 (함장의 손) 센서가 꺼진다
            string? dark = Notes(s => { var sm = s.Get<ShipMind>(); for (int r = 0; r < sm.RoomSeen.Length; r++) if (sm.RoomSeen[r] == 0 && s.Layout.Rooms[r].Cells.Length > 0) return r; return -1; },
                s => { var aim = HandTargets(s); s.Get<Hand>().Enqueue(Order(HandKind.PowerCut, aim.Power)); s.Run(3); }, "주컴퓨터가 그 방을 못 본다");
            if (dark == null) bad.Add("못 보는 방 까닭 없음");
            // 맡은 이가 막혔다: 맡긴 뒤 · 닿기 전에 그 로봇이 막힌 사실
            {
                var show = new LiveShow(HandShip(123), "디딤호") { FrameBudget = 1000 };
                var s = show.Sim; var m = s.Get<Mind>(); var aim = HandTargets(s);
                show.FastForward(5);
                s.Get<Hand>().Enqueue(Order(HandKind.Fire, aim.Bed, 1));
                Episode? e = null;
                for (int i = 0; i < 400 && (e = show.Dir.Episodes.LastOrDefault()) is not { AssignAt: >= 0 }; i++) show.FastForward(1);
                if (e != null && e.Assignee >= 0 && e.ArriveAt < 0)
                {
                    int door = s.Layout.Doors[0].Cell;
                    s.Facts.Emit(s.Tick, FactKind.Blocked, door, 0, s.Causes.Add(CauseKind.Setup, "문이 잠겨 들어가지 못한다", s.Tick, door), e.Assignee, 0);
                    show.FastForward(1);
                    if (!e.Why.Any(w => w.Contains($"{m.NameOf(e.Assignee)}") && w.Contains("막혔다") && w.Contains("문이 잠겨 들어가지 못한다"))) bad.Add($"막힘 까닭 [{string.Join(" · ", e.Why)}]");
                    // 닿음: 맡은 이가 그 방에 들면 그 틱 (연출은 맡은 이의 방만 본다 — 시험은 로봇을 그 방 가운데로 옮겨 놓는다)
                    var rc = s.Layout.Rooms[e.Room].Cells.Where(c => s.Layout.IsSpace(c)).ToArray();
                    m[e.Assignee].C.Cell = rc[rc.Length / 2];
                    long at0 = s.Tick;
                    show.FastForward(1);
                    if (e.ArriveAt != at0 + 1 && !(e.ArriveAt >= e.AssignAt && e.ArriveAt <= at0 + 1)) bad.Add($"닿음 {e.ArriveAt} (옮긴 틱 {at0})");
                }
                else bad.Add($"맡김 {e?.AssignAt} · 닿음 {e?.ArriveAt}");
            }
            Check("경과 E2: 늦은 까닭 (아는 것만) — 센서 선을 끊어 무선으로만 닿는 방의 불 '…은 무선 센서다 (36초마다 읽는다)' · 전기를 끊어 센서가 꺼진 방 '주컴퓨터가 그 방을 못 본다' · 맡은 로봇이 막힘 '… 막혔다 — 문이 잠겨 들어가지 못한다' / 맡은 이가 그 방에 들면 그 틱이 닿음",
                bad.Count == 0, bad.Count == 0 ? $"'{radio}' · '{dark}'" : string.Join(" | ", bad) + seen);
        }

        // E3: 결산 띠 6초 (끝나면 '침실 둘 불 — 11분 · 다친 이 1 · 끈 이 로봇 1') · 누르면 그 카드 · 견줌 줄 (같은 갈래 지난 경과 셋부터 — 평균) · 사건 띠 줄의 뿌리 → 그 카드
        {
            var bad = new List<string>();
            if (fire == null || fshow == null) bad.Add("E1 경과 없음");
            else
            {
                var show = fshow; var e = fire;
                string tally = show.Tally;
                if (!tally.StartsWith(e.Name + " — ") || !tally.Contains("분")) bad.Add($"결산 띠 '{tally}'");
                show.Paused = true; show.Advance(6.2); show.Paused = false;
                if (show.Tally.Length > 0) bad.Add("결산 띠가 6초 뒤에도 남았다");
                show.Release();
                show.TallyGo(e.Root);
                if (show.Card().FirstOrDefault()?.StartsWith("함장이 일으킨 불씨") != true) bad.Add("결산 띠를 눌러도 카드가 없다");
                // 견줌: 같은 갈래 지난 경과 둘이면 없다 · 셋이면 평균과 견준다
                long knew = Math.Min(e.NoticeAt < 0 ? long.MaxValue : e.NoticeAt, e.ComputerAt < 0 ? long.MaxValue : e.ComputerAt) - e.Start;
                Episode Past(int k, long delay) => new() { Root = 900_000 + k, Kind = e.Kind, Name = "식당 불", Title = "식당 불", Start = k, NoticeAt = k + delay, ComputerAt = k + delay, End = k + 1, Last = k + 1 };   // 이 불보다 먼저 끝난 불
                show.Dir.Episodes.Insert(0, Past(1, knew + 20)); show.Dir.Episodes.Insert(0, Past(2, knew + 20));
                if (show.Card().Any(l => l.StartsWith("견줌"))) bad.Add("지난 경과 둘인데 견줌 줄");
                show.Dir.Episodes.Insert(0, Past(3, knew + 20));
                string cmp = show.Card().FirstOrDefault(l => l.StartsWith("견줌")) ?? "";
                if (cmp != $"견줌 · 지난 불 셋보다 {Math.Round(20 * Time.SecondsPerTick)}초 빨리 알았다") bad.Add($"견줌 '{cmp}'");
                show.Dir.Episodes.RemoveAll(x => x.Root >= 900_000);
            }
            // 사건 띠 줄의 뿌리 (주컴퓨터 사건의 원인 사슬) → 그 카드
            {
                var show = new LiveShow(HandShip(124), "디딤호") { FrameBudget = 1000 };
                var s = show.Sim; var aim = HandTargets(s);
                show.FastForward(5);
                s.Get<Hand>().Enqueue(Order(HandKind.Fire, aim.Bed, 1));
                for (int i = 0; i < 300 && show.Feed().Length == 0; i++) show.FastForward(1);
                var feed = show.Feed();
                var row = feed.FirstOrDefault(x => x.Root != 0);
                var ep = show.Dir.Episodes.LastOrDefault();
                if (ep == null || row.Root != ep.Root) bad.Add($"사건 띠 뿌리 {row.Root} · 경과 {ep?.Root}");
                else
                {
                    show.FeedGo(row.Cell, row.Root);
                    if (show.Card().FirstOrDefault()?.StartsWith("함장이 일으킨 불씨") != true || show.Render().CardIs != CardKind.Episode) bad.Add("사건 띠 줄을 눌러도 카드가 없다");
                    show.SelectAgent(s.Get<Mind>().Agents.First(a => a.IsPerson).Id);
                    show.Release();
                    if (show.Card().Length > 0) bad.Add("카드 자리는 하나 — 사람을 고르고 놓으면 경과 카드도 닫힌다");
                }
            }
            Check("경과 E3: 끝나면 결산 띠 6초 ('…불 — n분 · …') · 누르면 그 카드 / 견줌 줄은 같은 갈래 지난 경과 셋부터 ('지난 불 셋보다 48초 빨리 알았다' — 평균) · 둘이면 없다 / 사건 띠 줄에 뿌리 → 누르면 그 카드 · 카드 자리는 하나",
                bad.Count == 0, bad.Count == 0 ? $"'{fshow?.Dir.Episodes.LastOrDefault()?.Name}'" : string.Join(" | ", bad));
        }

        // E4 · E5 (V7 · V8 검토): 감은 동안 끝난 경과 → 저장 · 이어 보기 → 첫 장에 그 결산 띠가 다시 뜨지 않는다 /
        // 같은 뿌리의 일이 다시 나 (꺼진 불이 다시 붙었다) 경과가 다시 열렸다가 다시 끝나면 결산 띠가 또 선다 (뿌리마다 한 번이 아니라 끝마다 한 번 — 이어 본 뒤에도)
        {
            var show = new LiveShow(HandShip(126), "디딤호") { FrameBudget = 1000 };
            var s = show.Sim; var aim = HandTargets(s);
            show.FastForward(5);
            s.Get<Hand>().Enqueue(Order(HandKind.Fire, aim.Bed, 1));
            Episode? e = null;
            for (int i = 0; i < 40 && (e = show.Dir.Episodes.LastOrDefault()) is not { End: >= 0 }; i++) show.FastForward(Time.Minutes(0.5));
            string ffTally = show.Tally;   // 감은 동안 끝났다 — 띠 없음
            var back = LiveShow.Resume(show.SaveBundle(SaveKind.Quit), s.Setup);
            back.Paused = true; back.Advance(0.05);   // 이어 본 첫 장 (멈춘 채)
            string first = back.Tally;
            Check("경과 E4: 감은 동안 끝난 경과 (띠 없이 적힘) → 저장 · 이어 보기 → 멈춘 채 첫 장에 그 경과의 결산 띠가 다시 뜨지 않는다",
                e is { End: >= 0 } && ffTally.Length == 0 && first.Length == 0,
                e == null ? "경과가 없다" : $"끝 {e.End} · 감은 뒤 띠 '{ffTally}' · 이어 본 첫 장 띠 '{first}'");

            var be = e == null ? null : back.Dir.EpisodeOf(e.Root);
            long end0 = be?.End ?? -1; string before = "", again = "", after = "";
            if (be is { End: >= 0 })
            {
                back.Advance(LiveShow.TallySec + 0.2);   // (멈춘 채) 앞 띠가 있었으면 사라지게 — 다시 끝난 띠만 본다
                before = back.Tally;
                var bs = back.Sim;
                bs.Facts.Emit(bs.Tick, FactKind.Ignition, be.Cell, 0, be.Root);   // 같은 뿌리의 일이 다시 났다 — 경과를 잇는다
                back.SetFixed(4);
                for (int i = 0; i < 60 && be.End <= end0; i++) back.Advance(0.2);   // 다시 열려 (끝 −1) 다시 끝날 때까지 (끝이 앞 끝보다 늦다)
                again = back.Tally;
                back.Paused = true; back.Advance(LiveShow.TallySec + 0.2); back.Advance(0.05);
                after = back.Tally;   // 6초 뒤 사라지고 다시 뜨지 않는다 (끝마다 한 번)
            }
            Check("경과 E5: 같은 뿌리의 일이 다시 나 경과가 다시 열렸다가 다시 끝나면 (이어 본 뒤) 결산 띠가 또 선다 ('…불 — n분 · …') · 6초 뒤 사라지고 다시 뜨지 않는다",
                be != null && before.Length == 0 && be.End > end0 && again.StartsWith(be.Name + " — ") && after.Length == 0,
                be == null ? "이어 본 경과가 없다" : $"앞 띠 '{before}' · 끝 {end0} → {be.End} · 띠 '{again}' · 6초 뒤 '{after}'");
        }

        // 손 결과 이름표 (§7.3): 겨눌 때 '불씨 · 보통 — 침실 둘 침대 · 이 방 사람 2 (1명 잠) · 가장 가까운 소화기 4m'
        {
            var show = new LiveShow(HandShip(125), "디딤호") { FrameBudget = 1000 };
            var s = show.Sim; var L = s.Layout; var m = s.Get<Mind>(); var life = s.Get<Life>(); var aim = HandTargets(s);
            show.FastForward(3);
            show.ArmHand(HandKind.Fire, 1);
            show.SetHoverAt(L.X(aim.Bed) + 0.5, L.Y(aim.Bed) + 0.5, 0.2);
            var a = show.HandAimNow();
            int room = L.Room[aim.Bed];
            int here = m.Agents.Count(x => x.IsPerson && x.C.Cell >= 0 && L.Room[x.C.Cell] == room && life[x.C.Body].Alive != 0);
            int asleep = m.Agents.Count(x => x.IsPerson && x.C.Cell >= 0 && L.Room[x.C.Cell] == room && life[x.C.Body].Alive != 0 && life[x.C.Body].Asleep != 0);
            var lockers = L.Furniture.Where(f => f.Cells.Length > 0 && m.Stock[f.Id * m.ItemCount + m.iExt] + m.Stock[f.Id * m.ItemCount + m.iCO2] > 0).ToList();   // 소화기가 든 선반 · 보관함
            double near = lockers.Count == 0 ? -1 : lockers.Min(f => Math.Sqrt(Math.Pow(L.X(f.Cells[0]) - L.X(aim.Bed), 2) + Math.Pow(L.Y(f.Cells[0]) - L.Y(aim.Bed), 2)));
            string want = $" · 이 방 사람 {here}" + (asleep > 0 ? $" ({asleep}명 잠)" : "");
            bool label = a.Ok && a.Label.StartsWith("불씨 · 보통 — ") && a.Label.Contains(want) && a.Label.EndsWith(near < 0 ? " · 남은 소화기 없음" : $" · 가장 가까운 소화기 {Math.Round(near):0}m");
            show.ArmHand(HandKind.Hole, 1);
            show.SetHoverAt(L.X(aim.Hull) + 0.5, L.Y(aim.Hull) + 0.5, 0.2);
            var b = show.HandAimNow();
            bool hole = b.Ok && b.Label.Contains(" · 이 방 사람 ") && !b.Label.Contains("소화기");
            Check("경과 손 이름표: 겨눌 때 '불씨 · 보통 — 침실 둘 침대 · 이 방 사람 2 (1명 잠) · 가장 가까운 소화기 4m' (선반 · 보관함에 남은 것) · 구멍은 곁 방 사람 (소화기 줄 없음)",
                label && hole, $"'{a.Label}' (방 사람 {here} · 잠 {asleep} · 소화기 {near:0.0}m) · '{b.Label}'");
        }

        // M5: 자리표 — 경과 카드 (왼쪽 · 가로 시계 · [▶ 다시보기] · [×]) · 결산 띠 (★ 띠 아래 · 알림 위 · 누를 자리) — 여섯 창 × 사람 3 · 12 · 40 · 100 × 글 100 · 150%: 겹침 0 · 밖 0 · 누를 자리 크기 · 글자
        {
            var hud = new Hud(); var bad = new List<string>(); int layouts = 0;
            string[] card = { "함장이 일으킨 불씨 — 침실 둘 침대", "1일째 00:06 — 끝 00:17 · 11분", "0:00 일어남 — 함장이 일으킨 불씨", "0:18 처음 안 이 — 김하늘 (코)", "0:24 주컴퓨터가 앎 — 연기 센서", "0:30 맡김 — 로봇 1",
                "1:10 닿음 — 로봇 1", "1:52 끔 — 로봇 1", "까닭 · 침실 둘은 무선 센서다 (36초마다 읽는다)", "아는 이 3/6 · 다친 이 1 (이도윤)", "견줌 · 지난 불 셋보다 40초 빨리 알았다" };
            var dots = new[] { new EpisodeDot(0, 0), new EpisodeDot(0.03f, 1), new EpisodeDot(0.04f, 2), new EpisodeDot(0.05f, 3), new EpisodeDot(0.11f, 4), new EpisodeDot(0.17f, 5), new EpisodeDot(1, 6) };
            foreach (var (pw, ph) in new[] { (1920, 1080), (1920, 1010), (1600, 900), (1366, 768), (1280, 720), (2560, 1440) })
                foreach (float scale in new[] { 1f, 1.5f })
                    foreach (int n in new[] { 3, 12, 40, 100 })
                        foreach (bool panel in new[] { false, true })
                        {
                            string tag = $"{pw}×{ph} {scale * 100:0}% 사람 {n}{(panel ? " 판" : "")}";
                            Hud.Measure ms = (t, px, b) => Hud.Approx(t, px, b) * scale; Hud.MeasureLines ml = (t, px, w, k) => Hud.ApproxLines(t, (int)(px * scale), w, k);
                            var f = FakeFrame(n, true, panel);
                            f.Card = card; f.CardIs = CardKind.Episode; f.Selected = -1; f.EpisodeDots = dots; f.EpisodeCanReplay = n % 2 == 0;
                            f.Tally = "침실 둘 불 — 11분 · 다친 이 1 · 끈 이 로봇 1"; f.TallyAlpha = 1;
                            f.Banner = "침실 둘 불이 번졌다"; f.BannerAlpha = 1; f.Notice = "함장이 일으켰다 — 불씨 · 보통 (침실 둘 침대)";
                            HudLayout.Build(f, new HudState(), pw, ph, ms, ml, hud); layouts++;
                            var c = hud.Area("카드"); var clock = hud.Area("경과 시계");
                            var rep = hud.Hits.FirstOrDefault(x => x.Act == UiAct.EpisodeReplay); var close = hud.Hits.FirstOrDefault(x => x.Act == UiAct.CardClose);
                            if (c.Empty || clock.Empty || !InsideR(clock, c) || rep == null || close == null || !InsideR(rep.R, c) || rep.R.H < 28 || rep.R.W < 100 || rep.Enabled != f.EpisodeCanReplay || hud.Hits.Any(x => x.Act is UiAct.CardFollow or UiAct.Star or UiAct.DetailOpen or UiAct.LookHand))
                                bad.Add($"{tag} 카드 {c} · 시계 {clock} · ▶ {rep?.R}");
                            var t = hud.Area("결산 띠"); var go = hud.Hits.FirstOrDefault(x => x.Act == UiAct.TallyGo);
                            if (t.Empty || go == null || go.R != t || t.H < 32) bad.Add($"{tag} 결산 띠 {t}");
                            else if (!(hud.Area("★ 띠").Bottom <= t.Y && t.Bottom <= hud.Area("알림").Y)) bad.Add($"{tag} 차례: ★ → 결산 → 알림");
                            foreach (var o in hud.Overlaps()) bad.Add($"{tag}: {o}");
                            foreach (var o in hud.Outside()) bad.Add($"{tag}: 밖 {o}");
                        }
            var texts = card.ToList();
            var glyph = texts.Where(t => t.Any(ch => Hud.Missing.Contains(ch))).ToList();
            Check("경과 M5: 경과 카드 (왼쪽 카드 자리 — 가로 시계 · [▶ 다시보기] · [×] · 사람 카드 단추 없음) · 결산 띠 (★ 띠 → 결산 → 알림 · 띠 전체가 누를 자리) — 여섯 창 × 사람 3 · 12 · 40 · 100 × 글 100 · 150% × 판: 겹침 0 · 밖 0 · 누를 자리 크기 · 글자",
                bad.Count == 0 && glyph.Count == 0, $"자리표 {layouts}가지 · 어긋남 {bad.Count} {string.Join(" | ", bad.Take(3))} · 글자 {string.Join(" ", glyph)}");
        }
    }
}
