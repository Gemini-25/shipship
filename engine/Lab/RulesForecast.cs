using ShipEngine;
using ShipView;

namespace Lab;

/// <summary>
/// 예측 장부 (docs/UI2.md §4.5 · §8 F1 ~ F6 — 묶음 '예측 장부'): 주컴퓨터의 화면용 예측 고리 (봉쇄 상상 · 미리 돌려 보기 · 걸음 시간 · 물자 · 대피)를
/// 관객의 장부가 맞댈 때 실제와 맞댄다 · 끼어든 일은 맞음 몫에서 뺀다 · 센서는 읽은 틱마다 방 하나씩 · 화면만 (쌍둥이 세계와 찍기 바이트가 같다).
/// </summary>
public static partial class Program
{
    private static void ForecastRules()
    {
        if (!Do("예측 장부")) return;
        Console.WriteLine("\n[예측 장부 — 봉쇄 상상 · 미리 돌려 보기 · 걸음 시간 · 끼어든 일 · 센서 · 화면만]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        PanelRow[] Predict(LiveShow show) { show.OpenDetail(DetailKind.Computer, -1); show.SetDetailSub(1); return show.PanelRows(); }

        // 봉쇄 상상 차림 (RulesComputer '상상 (가)'): 뚫린 방에 쓰러진 사람 · 열린 문 너머 옆 방에도 쓰러진 사람 → 지금 닫는다
        (LiveShow show, Dictionary<long, float[]> kpa, Sim? twin) SealWorld(Action<Sim, long>? mid = null, bool twin = false)
        {
            var s = Worlds.Full(lay, 1);
            var m = s.Get<Mind>(); var st = s.Get<Structure>(); var life = s.Get<Life>(); var f = s.Get<Field>();
            int a = m.AddPerson(s, At(6, 2)), b = m.AddPerson(s, At(13, 2));
            foreach (int p in new[] { a, b }) { ref var body = ref life[m[p].C.Body]; body.Conscious = 0; body.Hypoxia = 0.6f; }
            st.RequestDoor(lay.DoorAt[At(10, 3)], 1); s.Run(3);
            f.SetHole(At(0, 2), 0.05f);
            Sim? bs = null;
            if (twin) { bs = new Sim(s.Setup); bs.Load(s.Save()); }   // 쌍둥이 (같은 찍기 · 예측 고리를 켜지 않는다)
            var show = new LiveShow(s, "시험호");   // 판단 일지 · 주컴퓨터 일지 · 예측 고리를 켠다
            var kpa = new Dictionary<long, float[]>();
            long t0 = s.Tick;
            for (int k = 0; k < (int)Time.Minutes(3) + 140 && !show.Book.Closed.Any(x => x.G.Kind == GuessKind.Seal); k++)   // 봉쇄 상상을 맞댈 때까지
            {
                s.Step(); show.Book.Observe(s); bs?.Step();   // 연출은 돌리지 않는다 (장부만 — 짧게)
                kpa[s.Tick] = lay.Rooms.Select(r => r.Cells.Length > 0 ? (float)f.Pressure(r.Cells[0]) : 0f).ToArray();   // 틱 t 끝 = 다음 틱 가운데의 기압 (상상의 '그때')
                mid?.Invoke(s, s.Tick - t0);
            }
            return (show, kpa, bs);
        }

        var swT = System.Diagnostics.Stopwatch.StartNew();
        void Lap(string what) { if (Debug) Console.WriteLine($"    [{what}] {swT.ElapsedMilliseconds}ms"); swT.Restart(); }
        // ── F1 봉쇄 상상: 125틱 뒤 장부에 한 줄 (예측 · 실제 · 맞음) · 안 고른 미래 '맞댈 수 없다' · 그때 기압은 고리 값 · 두 그림 + 실제 ──
        {
            var (show, kpa, bs) = SealWorld(twin: true);
            var s = show.Sim; var book = show.Book;
            var seal = book.Closed.FirstOrDefault(x => x.G.Kind == GuessKind.Seal);
            var bad = new List<string>();
            if (seal == null) bad.Add($"맞댄 봉쇄 상상 없음 (열림 {book.Open.Count(x => x.G.Kind == GuessKind.Seal)} · 고리 {s.Get<ShipMind>().Guesses?.Count ?? 0})");
            else
            {
                if (seal.State is not (ForecastState.Right or ForecastState.Wrong)) bad.Add($"맞댐 {seal.State} {seal.Why}");
                if (seal.Closed != seal.G.Tick + ShipMind.ImagineTicks) bad.Add($"맞댄 틱 {seal.Closed} (상상 {seal.G.Tick} + 125)");
                if (seal.BeforeKPa == null || !kpa.TryGetValue(seal.G.Tick, out var then) || !seal.BeforeKPa.SequenceEqual(then)) bad.Add("그때 기압이 고리 값과 다르다");
                if (seal.Shot?.Wait == null || seal.Shot.Now == null || seal.Shot.Wait.RoomKPa.Length != lay.Rooms.Count || seal.Actual == null) bad.Add("두 그림 · 실제 그림 없음");
                if (seal.G.Verdict != 1) bad.Add("지금 닫음을 고르지 않았다");
            }
            var rows = Predict(show);
            bool Has(string t) => rows.Any(x => x.Main.Contains(t) || x.Sub.Contains(t));
            if (!Has("봉쇄 상상") || !Has("맞댈 수 없다") || !rows.Any(x => x.Boards is { Length: 3 })) bad.Add("예측 탭: " + string.Join(" | ", rows.Select(x => x.Main)));
            foreach (var x in rows) foreach (var t in new[] { x.Main, x.Sub }.Where(t => t.Length > 0)) if (LookBad(t) is { } why) bad.Add($"'{t}' {why}");
            if (Debug) Console.WriteLine("    " + string.Join("\n    ", rows.Select(x => x.Main + " / " + x.Sub.Replace("\n", " ⏎ "))));
            Check("예측 장부 F1 봉쇄 상상: 상상한 때 + 125틱에 실제와 맞댄다 (죽음 · 쓰러짐 · 다른 방이 잃은 기압 — 그때 기압은 장부 고리 값) · 고른 미래 · 안 고른 미래는 '맞댈 수 없다' · 두 그림 (기다리면 · 지금 닫으면)과 실제 · 예측 탭 · 개발 말 0",
                bad.Count == 0, bad.Count > 0 ? string.Join(" / ", bad.Take(4)) : $"예측 쓰러짐 {seal!.G.B} · 실제 {seal.ActB} · 기압 {seal.G.C:0.0} → {seal.ActC:0.0}kPa · {seal.State}");
            // ── F5 화면만: 장부 · 예측 고리 · 상상 그림을 켠 화면과 켜지 않은 쌍둥이 세계의 찍기 바이트가 같다 · 끈 세계에 예측 0 ──
            bool same = bs != null && s.Tick == bs.Tick && s.Save().AsSpan().SequenceEqual(bs.Save());
            int ring = s.Get<ShipMind>().Guesses?.Count ?? 0;
            Check("예측 장부 F5 화면만: 장부 · 예측 고리 · 상상 그림을 켠 화면 (봉쇄 상상)과 켜지 않은 쌍둥이 세계의 찍기 바이트가 같다 · 끈 세계에 예측 · 그림 0",
                same && ring > 0 && s.Get<ShipMind>().LastImagine != null && bs!.Get<ShipMind>().Guesses == null && bs.Get<ShipMind>().LastImagine == null,
                $"틱 {s.Tick}/{bs?.Tick} · 같음 {same} · 예측 고리 {ring} · 끈 세계 {bs?.Get<ShipMind>().Guesses?.Count ?? 0}");
        }

        Lap("F1 · F5");
        // ── F3 끼어든 일: 상상한 뒤 함장의 손 (폭발)을 끼우면 '끼어든 일' · 맞음 몫에서 빠진다 / 헛손 (배 밖을 누름 — 일어나지 않았다)은 끼어든 일이 아니다 ──
        (LiveShow show, Forecast? seal, HandOrder? done) HandWorld(int cell)
        {
            bool armed = false;
            var (show, _, _) = SealWorld((s, k) =>
            {
                if (armed || s.Get<ShipMind>().Guesses is not { Count: > 0 } g || g.Newest(0).Kind != GuessKind.Seal) return;
                armed = true;
                s.Get<Hand>().Enqueue(new HandOrder { Kind = HandKind.Blast, Cell = cell, Size = 0 });
            });
            HandOrder? done = null;
            foreach (var o in show.Sim.Get<Hand>().Done) if (o.Kind == HandKind.Blast) { done = o; break; }
            return (show, show.Book.Closed.FirstOrDefault(x => x.G.Kind == GuessKind.Seal), armed ? done : null);
        }
        {
            var (show, seal, done) = HandWorld(At(18, 2));   // 다른 방 (오른쪽 방)
            var (right, total) = show.Book.Summary();
            Check("예측 장부 F3 끼어든 일: 봉쇄 상상 뒤 다른 방에 함장이 일으킨 폭발 (원인 마디가 붙은 손) — 그 예측은 '끼어든 일'로 따로 두고 맞음 몫 (맞음 / 맞댐)에서 뺀다",
                done is { Cause: > 0 } && seal != null && seal.State == ForecastState.Interfered && seal.Why.Contains("함장이 일으킨") && show.Book.Interfered[(int)GuessKind.Seal] == 1 && total == show.Book.Right.Sum() + show.Book.Wrong.Sum(),
                $"손 {(done == null ? "없음" : $"원인 마디 {done.Value.Cause}")} · {seal?.State} '{seal?.Why}' · 맞음 {right}/{total}");
        }
        {
            var (show, seal, done) = HandWorld(-7);   // 배 밖 — 판정에 걸려 일어나지 않는다 (원인 마디 없이 손 장부에만)
            string why = done is { } d ? Hand.Why(show.Sim, d) : "";
            Check("예측 장부 F3 헛손: 봉쇄 상상 뒤 함장의 손이 판정에 걸려 일어나지 않았다 (원인 마디 0 · 세계 그대로) — 끼어든 일이 아니다 · 그 예측은 맞음 · 틀림으로 맞댄다",
                done is { Cause: 0 } && why.Length > 0 && seal != null && seal.State is ForecastState.Right or ForecastState.Wrong && seal.Interrupt.Length == 0 && show.Book.Interfered.Sum() == 0,
                $"손 {(done == null ? "없음" : $"원인 마디 {done.Value.Cause} '{why}'")} · {seal?.State} '{seal?.Why}'");
        }

        Lap("F3");
        // ── F2 미리 돌려 보기 · 걸음 시간 (매뉴얼 차림): 연다 → 5분 뒤 잃은 공기 실제 · 하지 않기로 한 것은 맞대지 않는다 / 걸음마다 예측 분 · 실제 분 ──
        {
            var legend = new Dictionary<char, string> { ['x'] = "OxygenMaskBox", ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer", ['e'] = "Bed", ['h'] = "HeatExchanger", ['k'] = "Shelf", ['w'] = "Workbench" };
            var ml = Worlds.Ascii(ManualShip, legend);
            var s = Worlds.Full(ml, 7);
            var m = s.Get<Mind>(); var f = s.Get<Field>(); var sm = s.Get<ShipMind>();
            var p = new List<int>();
            foreach (int x in new[] { 16, 17, 18, 23 }) { int a = m.AddPerson(s, Worlds.At(ml, x, 1)); m.SetPersonality(a, 0.5f, 0.7f, 0.6f, 0.5f, 0.3f); m[a].C.KnPressure = 0.5f; p.Add(a); }
            m.AddRobot(s, Worlds.At(ml, 22, 1), AgentKind.Robot, Worlds.At(ml, 22, 1));
            s.Run(2);
            s.Get<Network>().AirReserve = 2e6;
            var show = new LiveShow(s, "시험호");
            int hole = Worlds.At(ml, 3, 0);
            f.SetHole(hole, 0.02f);
            bool Enough() => show.Book.Closed.Any(x => x.G.Kind == GuessKind.Rehearse && x.G.Verdict == 1) && show.Book.Closed.Any(x => x.G.Kind == GuessKind.Step && x.State is ForecastState.Right or ForecastState.Wrong);
            for (int k = 0; k < (int)Time.Hours(3) && !Enough(); k++)   // 연 미리 돌려 보기 하나 · 걸음 하나를 맞댈 때까지
            {
                s.Step(); show.Book.Observe(s);
                foreach (int a in p) { ref var c = ref m[a].C; c.Sleepy = 0; c.Hunger = 0; c.Thirst = 0; s.Get<Life>()[c.Body].Asleep = 0; }
            }
            var book = show.Book;
            var reh = book.Closed.Where(x => x.G.Kind == GuessKind.Rehearse).ToList();
            var steps = book.Closed.Where(x => x.G.Kind == GuessKind.Step).ToList();
            var bad = new List<string>();
            if (reh.Count == 0) bad.Add("미리 돌려 보기 없음");
            foreach (var r in reh)
            {
                if (r.G.Verdict == 0 && (r.State != ForecastState.Unmatched || !r.Why.Contains("하지 않기로"))) bad.Add($"하지 않기로 한 것이 맞대졌다: {r.State}");
                if (r.G.Verdict == 1 && r.State == ForecastState.Open) bad.Add("연 것이 열린 채");
                if (r.G.Verdict == 1 && r.Opened && r.State is ForecastState.Right or ForecastState.Wrong && r.Closed != r.G.Tick + ShipMind.ImagineTicks) bad.Add($"맞댄 틱 {r.Closed - r.G.Tick}");
            }
            if (steps.Count == 0 || !steps.Any(x => x.State is ForecastState.Right or ForecastState.Wrong && x.ActA > 0)) bad.Add($"걸음 맞댐 없음 ({string.Join(" · ", steps.Select(x => $"{x.State} {x.ActA:0.0}/{x.G.A:0.0}"))})");
            var rows = Predict(show);
            bool Has(string t) => rows.Any(x => x.Main.Contains(t) || x.Sub.Contains(t));
            if (!Has("미리 돌려 보기") || !Has("걸음 시간")) bad.Add("예측 탭: " + string.Join(" | ", rows.Select(x => x.Main)));
            foreach (var x in rows) foreach (var t in new[] { x.Main, x.Sub }.Where(t => t.Length > 0)) if (LookBad(t) is { } why) bad.Add($"'{t}' {why}");
            if (Debug) Console.WriteLine("    " + string.Join("\n    ", rows.Select(x => x.Main + " / " + x.Sub.Replace("\n", " ⏎ "))));
            Check("예측 장부 F2 미리 돌려 보기 · 걸음 시간: 연다고 한 것은 5분 뒤 쓰러짐 · 잃은 공기를 실제와 맞대고 · 하지 않기로 한 것은 맞대지 않는다 / 걸음마다 예측 분과 실제 분 · 예측 탭 줄 · 개발 말 0",
                bad.Count == 0, bad.Count > 0 ? string.Join(" / ", bad.Take(4)) : $"미리 돌려 보기 {reh.Count} ({string.Join(" · ", reh.Select(x => $"{(x.G.Verdict == 1 ? "연다" : "안 함")} {x.State}"))}) · 걸음 {steps.Count} ({string.Join(" · ", steps.Take(4).Select(x => $"{x.G.A:0.0}→{x.ActA:0.0}분 {x.State}"))})");
        }

        Lap("F2");
        // ── F4 센서 맞대기: 선 방은 어긋남 0 · 못 본 방은 따로 센다 (외판 구멍으로 기압이 움직이는 동안 · 전선을 끊어 한 방을 못 본 뒤) ──
        {
            var s = Worlds.Full(lay, 3);
            var net = s.Get<Network>(); var L = s.Layout;
            int power = -1;
            foreach (var fd in net.Feeders)
            {
                foreach (int c in fd.Path) if (L.Room[c] == fd.Room && L.IsSpace(c) && (net.LineAt[c] & Network.Bit(LineKind.Power)) != 0 && net.Devices.All(d => d.Type != "MainComputer" || d.Room != fd.Room)) { power = c; break; }
                if (power >= 0) break;
            }
            var show = new LiveShow(s, "시험호");
            s.Get<Field>().SetHole(At(0, 2), 0.02f);
            long obs = 0; int nObs = 0;
            int seen = 0;
            void Tick() { s.Step(); long t0 = System.Diagnostics.Stopwatch.GetTimestamp(); show.Book.Observe(s); if (++seen > 10) { obs += System.Diagnostics.Stopwatch.GetTimestamp() - t0; nObs++; } }   // 처음 열 번은 데우기
            for (int k = 0; k < 40; k++) Tick();
            if (power >= 0) s.Get<Hand>().Enqueue(new HandOrder { Kind = HandKind.PowerCut, Cell = power, Size = 1 });
            for (int k = 0; k < 30; k++) Tick();
            double obsUs = obs * 1e6 / System.Diagnostics.Stopwatch.Frequency / Math.Max(1, nObs);
            var book = show.Book;
            Check("예측 장부 F4 센서 맞대기: 주컴퓨터가 읽은 틱마다 방 하나씩 그 틱 앞 참값과 견준다 — 선으로 읽은 방은 어긋남 0 (같은 셈 · 외판 구멍으로 기압이 움직이는 동안) · 못 본 방은 따로 센다 (전선을 끊은 뒤)",
                power >= 0 && book.SensorWire > 8 && book.SensorWireRight == book.SensorWire && book.SensorBlind > 0,
                $"선 {book.SensorWireRight}/{book.SensorWire} · 무선 {book.SensorRadioRight}/{book.SensorRadio} · 못 봄 {book.SensorBlind} · 장부 틱마다 {obsUs:0.0}µs (방 {L.Rooms.Count})");
        }

        // ── F6 물자 날: 주컴퓨터가 한 시간마다 셈해도 장부는 하루에 하나 (하루 창이 겹치지 않게) · 지금 하는 예측에 물자 한 줄 (마지막 셈) · 넉넉한 날도 하루에 한 줄 ──
        {
            var s = Worlds.Full(lay, 2);
            var show = new LiveShow(s, "시험호");
            var sm = s.Get<ShipMind>(); var book = show.Book;
            long t0 = s.Tick;
            // 하루를 돌리지 않고 화면용 고리에 한 시간마다의 셈을 넣는다 (장부는 고리와 틱만 읽는다): 첫날은 줄어드는 셈 스물넷 · 이튿날은 넉넉한 셈 스물넷
            for (int h = 0; h < 48; h++)
            {
                bool rich = h >= 24;
                (sm.Guesses ??= new Ring<Guess>(128)).Add(new Guess { Tick = t0 + h * Time.TicksPerHour, Kind = GuessKind.Supply, Room = -1, Obj = 2, Incident = -1, A = rich ? 999 : 40 - h, B = rich ? 999 : 15, Verdict = 0 });
                book.Observe(s);
                if (h == 23)
                {
                    var rows = Predict(show);
                    int open = book.Open.Count(x => x.G.Kind == GuessKind.Supply), lines = rows.Count(x => x.Main.StartsWith("물자 —"));
                    bool last = rows.Any(x => x.Main.StartsWith("물자 —") && x.Main.Contains("식량 17일"));   // 마지막 셈 (40 − 23)
                    Check("예측 장부 F6 물자 날: 한 시간마다 셈해도 장부가 받는 것은 하루에 하나 (하루 창이 겹치지 않게) · 지금 하는 예측에 물자 줄 하나 (마지막 셈의 날)",
                        open == 1 && lines == 1 && last, $"열린 물자 {open} · 물자 줄 {lines} · 마지막 셈 {last} ({string.Join(" | ", rows.Where(x => x.Main.StartsWith("물자")).Take(2).Select(x => x.Main))})");
                }
            }
            int closed = book.Closed.Count(x => x.G.Kind == GuessKind.Supply);
            Check("예측 장부 F6 넉넉한 날: 넉넉한 셈 (맞대지 않는 것)도 장부에는 하루에 한 줄 — 지난 예측과 장부 200줄을 물자가 밀어내지 않는다",
                closed == 1 && book.Unmatched[(int)GuessKind.Supply] == 1, $"지난 물자 줄 {closed} · 맞댈 수 없는 것 {book.Unmatched[(int)GuessKind.Supply]}");
        }
    }
}
