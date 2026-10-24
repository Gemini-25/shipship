using System.Diagnostics;
using System.Text;
using ShipEngine;

namespace Lab;

public static partial class Program
{
    /// <summary>큰 시험 배: 방 (cols × rows)개, 방마다 8×6 · 사이 문 · 가운데 통로.</summary>
    internal static string BigShip(int cols, int rows)
    {
        int rw = 9, rh = 7;
        int W = cols * rw + 1, H = rows * rh + 1;
        var g = new char[H, W];
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) g[y, x] = '.';
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            if (x % rw == 0 || y % rh == 0) g[y, x] = '#';
        for (int cy = 0; cy < rows; cy++)
            for (int cx = 0; cx < cols; cx++)
            {
                int x0 = cx * rw, y0 = cy * rh;
                if (cx + 1 < cols) g[y0 + rh / 2, x0 + rw] = 'D';
                if (cy + 1 < rows) g[y0 + rh, x0 + rw / 2] = (cx + cy) % 3 == 0 ? 'P' : 'D';
            }
        var sb = new StringBuilder();
        for (int y = 0; y < H; y++) { for (int x = 0; x < W; x++) sb.Append(g[y, x]); sb.Append('\n'); }
        return sb.ToString();
    }

    /// <summary>--fingerprint: 배 몇 척을 몇 분 돌린 세계 지문 — 속도만 고친 뒤 결과가 한 비트도 안 바뀌었는지 (고치기 전과 견준다).</summary>
    private static void Fingerprints()
    {
        if (Environment.GetCommandLineArgs().Contains("flow")) { FlowPrints(); return; }
        foreach (var (ship, people, robots, ticks) in new[] { ("Didim", 6, 2, 3000), ("Salpim", 12, 4, 1500), ("Saetteul", 40, 10, 600), ("Hannuri", 100, 30, 300) })
        {
            var s = ShipWorld.Crewed(ship, 7, people, robots);
            s.Run(ticks);
            Console.WriteLine($"{ship} {people}/{robots} {ticks}틱 {s.Hash():x16}");
        }
    }

    /// <summary>
    /// --fingerprint flow: 사실 흐름 지문 — 세계에 일어난 일(사실의 틱 · 종류 · 칸 · 누가 · 상대 · 원인 글) · 원인 마디 수 · 행동(틱마다 사람 · 로봇의 칸 · 목표 · 대상) ·
    /// 물건(틱마다 바닥 물건의 칸 · 물건 · 남은 양 비트 · 막은 구멍 — 정렬해서: 다리가 물건을 다른 칸에 두면 바뀐다 · docs/PHYSICS.md §9)만 견준다 (항해 중).
    /// 무게만 바뀌는 고침(예: 소리 누수 — 찍기 바이트가 줄어 `--fingerprint`는 바뀐다)에서 '일어난 일은 한 비트도 안 바뀌었다'를 보인다 (함장 결정 · REVIEW.md 질문 26).
    /// </summary>
    private static void FlowPrints()
    {
        foreach (var (ship, people, robots, ticks) in new[] { ("Didim", 6, 2, 36000), ("Salpim", 12, 4, 12000), ("Saetteul", 40, 10, 3000) })
        {
            var s = ShipWorld.Crewed(ship, 7, people, robots);
            ShipWorld.Embark(s, ShipWorld.DefaultRoute);   // 항해 중 — 자연 사건 · 사고가 난다
            var mind = s.Get<Mind>(); var st = s.Get<Structure>();
            ulong h = 1469598103934665603UL, act = 1469598103934665603UL, things = 1469598103934665603UL; long cursor = 0, n = 0;
            void Mix(long v) { h ^= (ulong)v; h *= 1099511628211UL; }
            void Act(long v) { act ^= (ulong)v; act *= 1099511628211UL; }
            void Thing(long v) { things ^= (ulong)v; things *= 1099511628211UL; }
            var loose = new List<(int cell, byte item, int charge, int plug)>();
            for (int t = 0; t < ticks; t++)
            {
                s.Step();
                foreach (var a in mind.Agents) { Act(a.C.Cell); Act((long)a.C.Goal); Act(a.C.GoalTarget); }   // 행동: 사람 · 로봇이 어디서 무엇을 하나 (틱마다)
                loose.Clear();   // 물건: 바닥에 놓인 것 (④ 물체 장부 — 차례는 장부 차례가 아니라 정렬: 옛 마음 목록과 같은 값을 낸다)
                foreach (var it in st.Props) if (it.Kind == 0) loose.Add((st.CellOf(it), (byte)it.What, BitConverter.SingleToInt32Bits(it.Charge), it.PlugAt));
                loose.Sort();
                foreach (var (cell, item, charge, plug) in loose) { Thing(cell); Thing(item); Thing(charge); Thing(plug); }
                Thing(-1);   // 틱 끝 (물건이 없는 틱도 센다)
                foreach (var f in s.Facts.Since(cursor))
                {
                    Mix(f.Tick); Mix((long)f.Kind); Mix(f.Cell); Mix(f.Actor); Mix(f.Other); Mix(BitConverter.SingleToInt32Bits(f.Size));
                    if (f.Cause != 0) foreach (char ch in s.Causes.Text(f.Cause)) Mix(ch);
                    n++;
                }
                cursor = s.Facts.Total;
            }
            Console.WriteLine($"{ship} {people}/{robots} {ticks}틱 사실 {n} · 원인 마디 {s.Causes.Count} · 흐름 {h:x16} · 행동 {act:x16} · 물건 {things:x16} · 세계 {s.Hash():x16}");
        }
    }

    /// <summary>
    /// --budget: 엔진별 시간 예산 (docs/DEPTH.md ① — CI가 따로 잰다). 새터호 40명 · 로봇 10은 한 틱 4ms 안 · 엔진마다 제 몫 안 · 한누리호 100명 · 로봇 30은 9ms 안.
    /// 세 번 재어 가장 빠른 것을 쓴다 (같은 기계의 다른 일에 흔들리지 않게). 넘으면 1로 끝난다 (CI 빨강).
    /// </summary>
    private static void Budget()
    {
        var share = new Dictionary<string, double>
        {
            ["장"] = 1.8, ["마음"] = 1.0, ["망"] = 0.6, ["구조"] = 0.4, ["화학"] = 0.4, ["주컴퓨터"] = 0.4,
        };
        const double Other = 0.15; // 나머지 엔진은 저마다
        (double total, Dictionary<string, double> per) Measure(string ship, int people, int robots, int ticks)
        {
            var s = ShipWorld.Crewed(ship, 7, people, robots);
            s.Run(300); // 데우기 (처음 길찾기 · 계획)
            (double total, Dictionary<string, double> per) best = (double.MaxValue, new());
            for (int k = 0; k < 3; k++)
            {
                s.Measure = true; s.LastMs.Clear();
                var sw = Stopwatch.StartNew();
                s.Run(ticks);
                double ms = sw.Elapsed.TotalMilliseconds / ticks;
                if (ms < best.total) best = (ms, s.LastMs.ToDictionary(kv => kv.Key, kv => kv.Value / ticks));
            }
            s.Measure = false;
            return best;
        }
        Console.WriteLine($"\n[① 시간 예산 — 코어 {Par.Threads}]");
        var (t, per) = Measure("Saetteul", 40, 10, 300);
        Console.WriteLine("  " + string.Join(" · ", per.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:0.000}/{share.GetValueOrDefault(kv.Key, Other):0.00}")));
        var over = per.Where(kv => kv.Value > share.GetValueOrDefault(kv.Key, Other)).Select(kv => $"{kv.Key} {kv.Value:0.00}ms > {share.GetValueOrDefault(kv.Key, Other):0.00}").ToList();
        Check("시간 예산: 새터호 40명 · 로봇 10 — 한 틱 4ms 안 (60프레임에 넉넉히)", t <= 4.0, $"{t:0.00}ms/틱 (하루 = {t * Time.TicksPerDay / 1000:0}초)");
        Check("시간 예산: 엔진마다 제 몫 안 (장 1.8 · 마음 1.0 · 망 0.6 · 구조 · 화학 · 주컴퓨터 0.4 · 나머지 0.15ms)", over.Count == 0, over.Count == 0 ? "모두 안" : string.Join(" · ", over));
        // 판단 깊이의 잣대 (docs/MIND.md 성능 예산): 사람 한 명이 쓰는 마음 몫 · 위기 틱의 마음 — 판단 층을 더할 때마다 이 줄로 값을 치른다
        double perPerson = per.GetValueOrDefault("마음") / 40;
        Check("시간 예산: 새터호 조용한 틱 — 마음 ÷ 사람 수 ≤ 0.012ms (판단 층을 더해도 사람 한 명 몫)", perPerson <= 0.012, $"{perPerson * 1000:0.0}µs/사람 (마음 {per.GetValueOrDefault("마음"):0.000}ms · 40명)");
        // 위기 틱: 같은 위기를 두 번 — 첫 번은 데우기 (불 끄기 · 뿌리기 · 불 느끼기 코드가 이 과정에서 처음 돌며 JIT 번역에 한 번 7~11ms — 틱마다의 값이 아니다) · 둘째 번을 잰다
        double coldMax = 0;
        // 데우기 (6단 — 게임은 배를 띄울 때 한다): 메서드 미리 옮기기 + 다른 씨앗 새뜰 복제로 위기 길 한 번 → 그 뒤 첫 위기를 잰다 (BENCH_COLD=1이면 데우지 않는다 — 견주기용)
        if (Environment.GetEnvironmentVariable("BENCH_COLD") != "1")
        {
            ShipView.Warmup.Start(); ShipView.Warmup.Job?.Join();
            var wc = ShipWorld.Crewed("Saetteul", 9, 40, 10); wc.Run(30); ShipView.Warmup.Crisis(wc); ShipView.Warmup.CrisisJob?.Join();
            Console.WriteLine($"  데우기: 코드 미리 옮기기 {ShipView.Warmup.Methods}개 · {ShipView.Warmup.Ms:0}ms · 위기 길 {ShipView.Warmup.CrisisMs:0}ms");
        }
        for (int pass = 0; pass < 2; pass++)
        {
            var s = ShipWorld.Crewed("Saetteul", 7, 40, 10);
            s.Run(300);
            var room = s.Layout.Rooms.Where(r => r.Cells.Length > 30 && r.Type != "Corridor").OrderByDescending(r => r.Cells.Length).First();
            var chem = s.Get<Chemistry>();
            foreach (int c in room.Cells.Where((c, i) => i % 9 == 0).Take(4)) chem.Ignite(c, 3e6, s.Causes.Add(CauseKind.Setup, "시험 불", s.Tick, c));
            var mindMs = new List<double>();
            bool probe = pass == 1 && Environment.GetEnvironmentVariable("CRISIS_PROF") == "1";   // 조사용: 가장 긴 틱 다섯의 마음 안 단계 · 사람 · 로봇마다 (GC 횟수와 함께)
            var ticks = new List<(double ms, Dictionary<string, double> d)>();
            s.Measure = true;
            for (int k = 0; k < 400; k++)
            {
                if (probe) Mind.Prof = new();
                int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
                s.LastMs.Clear(); s.Step(); mindMs.Add(s.LastMs.GetValueOrDefault("마음"));
                if (probe) { Mind.Prof!["GC0"] = GC.CollectionCount(0) - g0; Mind.Prof["GC1"] = GC.CollectionCount(1) - g1; Mind.Prof["GC2"] = GC.CollectionCount(2) - g2; ticks.Add((mindMs[^1], Mind.Prof)); }
            }
            s.Measure = false;
            if (probe) { Mind.Prof = null; foreach (var (ms, d) in ticks.OrderByDescending(x => x.ms).Take(5)) Console.WriteLine($"    {ms:0.00}ms: " + string.Join(" · ", d.Where(kv => !kv.Key.StartsWith("GC")).OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Key} {kv.Value:0.00}")) + $" · GC {d["GC0"]}/{d["GC1"]}/{d["GC2"]}"); }
            mindMs.Sort();
            if (pass == 0)
            {
                coldMax = mindMs[^1];
                int over3 = mindMs.Count(v => v > 3.0);
                Console.WriteLine($"  첫 위기 (데우기 {(Environment.GetEnvironmentVariable("BENCH_COLD") == "1" ? "없음" : "뒤")}): 3ms 넘는 틱 {over3} · 가장 긴 {coldMax:0.00} · 둘째 {mindMs[^2]:0.00}ms");
                if (Environment.GetEnvironmentVariable("BENCH_COLD") != "1")
                    Check("시간 예산: 데운 뒤 첫 위기 — 마음 3ms 넘는 틱이 하나 넘지 않는다 (데우기 없으면 JIT 번역으로 여럿 · 한 번 튐은 공유 기계의 GC · 스케줄 몫으로 봐준다)", over3 <= 1, $"3ms 넘는 틱 {over3} · 가장 긴 {coldMax:0.00} · 둘째 {mindMs[^2]:0.00}ms");
                continue;
            }
            double p99 = mindMs[(int)(mindMs.Count * 0.99)], max = mindMs[^1];
            Check($"시간 예산: 위기 틱 (새터호 {room.Name} 불 넷 · 40명이 달아나고 · 끄고 · 구하고) — 마음 ≤ 2ms (99번째 백분위 · 데운 뒤)", p99 <= 2.0, $"99% {p99:0.00}ms · 가장 긴 틱 {max:0.00}ms · 가운데 {mindMs[mindMs.Count / 2]:0.000}ms · (처음 한 번 — JIT 번역을 셈한 가장 긴 틱 {coldMax:0.00}ms)");
        }
        var (th, _) = Measure("Hannuri", 100, 30, 150);
        Check("시간 예산: 한누리호 100명 · 로봇 30 — 한 틱 9ms 안", th <= 9.0, $"{th:0.00}ms/틱");
        // 그리기 예산 (6단 60프레임 · docs/GRAPHICS.md §3): 세계 스레드가 나아가기 + 그리기를 1/60초 안에 — 넓은 보기는 칸당 16 · 8도트 (굽은 덩어리)
        // 줄마다 세 번 재어 가장 작은 가운데 값으로 판정한다 (예산 고침 — 한가한 기계 · 같은 판에서도 한 번 잰 값이 4.2 ~ 7.1ms로 흔들렸다: 다른 일 · VM 빼앗김은 시간을 더할 뿐 빼지 않는다).
        // 기준 숫자는 그대로 (느슨하게 하는 것이 아니라 잡음을 뺀다) · 줄에는 고른 값 (세 가운데 값)을 함께 적는다 · DRAW_PROF=1이면 넓은 보기 단계마다 (조사용)
        // 세 회차는 32 · 16 · 8도트 줄을 돌아가며 잰다 (Rounds — 예산 고침 검토: 한 줄의 세 회차를 곧바로 이어 재면 데운 직후의 단계 번역 구간이 셋을 다 덮었다)
        {
            // 재기 점검 (예산 고침 검토 — 첫 줄 32도트 (7.8/10.5/6.0) · (6.8/8.3/3.8)ms · 평소 3.7 ~ 4.1 · 단계 번역을 끄면 3.7 · 4.1 · 5.0ms):
            //    가짜 줄 셋 (한 장 4 · 7 · 4ms — 가짜 시계로 센다 · 처음 0.6초는 두 배 — 데운 직후의 단계 번역)을 같은 재기로 재면 줄마다 고른 값이 평소 값이다
            {
                double clock = 0;
                (Action, Func<double>) Fake(double ms) { double One() { double v = clock < 600 ? 2 * ms : ms; clock += v; return v; } return (() => One(), One); }
                var (fa, fb, fc) = Rounds(Fake(4), Fake(7), Fake(4));
                Check("그리기 예산 재기: 처음 0.6초가 두 배 느린 가짜 줄 셋 (한 장 4 · 7 · 4ms — 데운 직후의 단계 번역 · 가짜 시계) — 줄마다 고른 값이 평소 값 (한 줄의 회차 사이에 다른 줄의 재기가 낀다)",
                    fa.V == 4 && fb.V == 7 && fc.V == 4, $"{fa:0.0} · {fb:0.0} · {fc:0.0}ms (가짜 시계 {clock / 1000:0.00}초)");
            }
            var s = ShipWorld.Crewed("Saetteul", 9, 40, 16, 4);
            s.Run(300);
            var p = new ShipView.Painter(s.Layout);
            int W = s.Layout.W, H = s.Layout.H;
            var into = new ShipSim.Art.Img(97 * 32, 55 * 32);
            bool prof = Environment.GetEnvironmentVariable("DRAW_PROF") == "1";
            void Near() => p.Frame(s, W / 2 - 20, H / 2 - 12, 41, 24, 3, into);
            void Far16() => p.FrameFar(s, W / 2 - 48, H / 2 - 27, 97, 55, 3, 16, into);
            void Far8() => p.FrameFar(s, -1, -1, W + 2, H + 2, 3, 8, into);
            (Action, Func<double>) Med(Action f) => (f, () => { var sw = Stopwatch.StartNew(); f(); return sw.Elapsed.TotalMilliseconds; });   // 한 장
            (Action, Func<double>) Stage(Action f, string key) => (f, () => { ShipView.Painter.Prof = new(); f(); double v = ShipView.Painter.Prof.GetValueOrDefault(key); ShipView.Painter.Prof = null; return v; });   // 그 단계 (Painter.Prof — 실험실만)
            p.FrameFar(s, -1, -1, W + 2, H + 2, 0, 8, into);   // 처음 한 번 모두 굽는다 (관전 화면은 32도트로 보는 동안 미리 굽는다)
            var (near, far16, far8) = Rounds(Med(Near), Med(Far16), Med(Far8));
            if (prof) { StageProf("평시 16도트", Far16); StageProf("평시 8도트", Far8); }
            Check("그리기 예산: 새뜰호 41×24칸 32도트 ≤ 6ms · 97×55칸 16도트 ≤ 8ms · 배 전체 8도트 ≤ 8ms (가운데 값 — 세계 스레드가 1/60초 안에)",
                near <= 6 && far16 <= 8 && far8 <= 8, $"{near:0.0} · {far16:0.0} · {far8:0.0}ms (덩어리 {p.Baked})");
            // 평시 판 (습도 · 곰팡이가 없다 — 300틱은 안개 칸이 하나뿐): 5단 A2가 더한 '김 · 안개' 단계가 장마다 0에 가깝다 — 같은 틱을 다시 그린 장 (관전 화면이 Painter.Still을 켠다 — 멈춤 · 느린 연출 · 따라 보기)은
            //    안개 칸 목록을 다시 쓴다 (예산 고침 — 바닥 칸마다 RelHumidity (나눗셈 여럿 · exp)를 셈하던 것: 32 · 16 · 8도트 0.03 · 0.12 · 0.15ms ·
            //    예산 고침 검토 — 같은 틱에도 장마다 바닥 칸을 모두 훑던 것: 0.015 · 0.06 · 0.07ms가 판마다 0.052 ~ 0.124ms로 흔들려 문턱 0.1ms에 여유가 없었다).
            //    틱이 지난 장 (다시 훑는다 — 칸마다 기체 성분 열일곱을 읽는 값)은 함께 적는다 (판정하지 않는다 — 세계 값을 읽는 몫)
            p.Still = true;
            var (qNear, qFar16, qFar8) = Rounds(Stage(Near, "김 · 안개"), Stage(Far16, "넓은: 김 · 안개"), Stage(Far8, "넓은: 김 · 안개"));
            p.Still = false;
            var (rNear, rFar16, rFar8) = Rounds(Stage(Near, "김 · 안개"), Stage(Far16, "넓은: 김 · 안개"), Stage(Far8, "넓은: 김 · 안개"));
            Check("그리기 예산: 평시 판 (습도 · 곰팡이 없음) — 같은 틱을 다시 그린 장의 '김 · 안개' 단계가 0에 가깝다 (32 · 16 · 8도트 ≤ 0.1ms)",
                qNear <= 0.1 && qFar16 <= 0.1 && qFar8 <= 0.1, $"{qNear:0.000} · {qFar16:0.000} · {qFar8:0.000}ms (틱이 지난 장 — 다시 훑는다 {rNear.V:0.000} · {rFar16.V:0.000} · {rFar8.V:0.000})");
            // 습한 판 (5단 A2 검토 — 평시 300틱은 안개 칸이 하나뿐이라 안개 비용을 못 봤다 · 3000틱이면 100칸): 칸의 수증기를 습도 0.97로 (RulesCracks 응력 부식의 식)
            // ① 평시에 습한 방 (화장실 · 운동실 · 재배 갑판 · 휴게실 — 새뜰호 3000틱의 안개 칸이 여기에)의 바닥 — 같은 창 · 같은 예산
            // ② 제습기가 멈춘 배 (바닥 칸 모두 — 보이는 바닥마다 안개): '김 · 안개' 단계만 잰다 (다른 층의 흔들림에 묻히지 않게 · 고치기 전 10.4 · 18.2 · 12.5ms)
            var fd = s.Get<Field>();
            int Humid(IEnumerable<int> cells)
            {
                int n = 0;
                foreach (int c in cells)
                {
                    double tk = fd.Temp(c), want = 0.97 * Water.SatKPa(tk) * 1000 * Field.CellVolume / (Field.R * tk), have = fd.Gas(fd.T.gH2O, c);
                    if (want > have) fd.AddGas(c, fd.T.gH2O, want - have, tk); else fd.RemoveGas(c, fd.T.gH2O, have - want);
                    if (fd.RelHumidity(c) > ShipView.Painter.FogRh) n++;
                }
                return n;
            }
            bool IsFloor(int c) => s.Layout.Kind[c] == CellKind.Floor;
            int wet = Humid(s.Layout.Rooms.Where(r => r.Type is "Washroom" or "Gym" or "Hydroponics" or "Lounge").SelectMany(r => r.Cells).Where(IsFloor));
            var (hNear, hFar16, hFar8) = Rounds(Med(Near), Med(Far16), Med(Far8));
            Check("그리기 예산: 습한 판 — 평시에 습한 방 (화장실 · 운동실 · 재배 갑판 · 휴게실) 바닥을 습도 0.97로 (안개) · 같은 창 · 같은 예산 (32도트 ≤ 6 · 16 · 8도트 ≤ 8ms)",
                wet >= 600 && hNear <= 6 && hFar16 <= 8 && hFar8 <= 8, $"안개 칸 {wet} · {hNear:0.0} · {hFar16:0.0} · {hFar8:0.0}ms (평시 {near.V:0.0} · {far16.V:0.0} · {far8.V:0.0})");
            var floors = Enumerable.Range(0, s.Layout.N).Where(IsFloor).ToArray();
            int all = Humid(floors);
            var (sNear, sFar16, sFar8) = Rounds(Stage(Near, "김 · 안개"), Stage(Far16, "넓은: 김 · 안개"), Stage(Far8, "넓은: 김 · 안개"));
            var (aNear, aFar16, aFar8) = Rounds(Med(Near), Med(Far16), Med(Far8));
            if (prof) StageProf("안개 16도트", Far16);
            Check("그리기 예산: 제습기가 멈춘 배 (바닥 칸 모두 습도 0.97 — 보이는 바닥마다 안개) — '김 · 안개' 단계 32도트 ≤ 1.5 · 16 · 8도트 ≤ 2ms",
                all >= floors.Length * 9 / 10 && sNear <= 1.5 && sFar16 <= 2 && sFar8 <= 2, $"안개 칸 {all}/{floors.Length} · 단계 {sNear:0.00} · {sFar16:0.00} · {sFar8:0.00}ms (한 장 {aNear:0.0} · {aFar16:0.0} · {aFar8:0.0}ms)");
            // ③ 곰팡이 핀 배 (5단 A3 검토 — 제습기가 멈춘 배는 278~310K이면 하루 0.7배씩 자라 약 일주일 뒤 바닥이 모두 곰팡이 1 · 안개도 그대로): 바닥 · 문 칸 모두 Mold 1
            //    32도트: 곰팡이를 그리는 단계 (바닥 · 벽 · 문 + 칸 덮개)가 곰팡이 0보다 0.5ms 안 · 한 장 ≤ 6ms
            //    넓은 보기: 곰팡이 단이 바뀐 덩어리는 다시 굽는다 (한 장에 FarBakeMs까지 — 덩어리 하나는 늘 굽는다) → 덩어리 하나를 굽는 곰팡이 단계가 곰팡이 0보다 0.06ms 안 · 다 구운 뒤 16 · 8도트 ≤ 8ms
            //    (고치기 전 — 칸마다 점 수백 개를 소수로 섞었다: 단계 0.59 → 3.92ms · 덩어리 0.082 → 0.197ms)
            //    넓은 보기 한 장은 평시 판 + 1ms 안 (예산 고침 — 문 칸 곰팡이를 장마다 32도트 판에 섞어 줄이고 · 안개 칸마다 습도를 두 번 셈하고 · 네 점씩 섞던 것: 16 · 8도트 +0.8~1.4 · +0.7~1.0ms)
            var lf = s.Get<Life>();
            var moldy = Enumerable.Range(0, s.Layout.N).Where(c => s.Layout.Kind[c] is CellKind.Floor or CellKind.Door).ToArray();
            void Mold(float v) { foreach (int c in moldy) lf.Mold[c] = v; }
            string[] moldStages = { "바닥 · 벽 · 문", "칸 덮개" };   // 곰팡이를 그리는 단계 (바닥 칸은 바닥 타일과 함께 · 문 칸은 칸 덮개 — 굽는 덩어리도 같은 Draw)
            double MoldMs() => moldStages.Sum(key => ShipView.Painter.Prof!.GetValueOrDefault(key));
            static double Mid(List<double> v) { v.Sort(); return v.Count == 0 ? double.NaN : v[v.Count / 2]; }
            // 곰팡이 0 · 1을 한 장씩 번갈아 잰다 (함께 쓰는 기계의 흔들림이 두 쪽에 같게) — 32도트 곰팡이 단계 합의 가운데 값 (24장을 세 번 — 쪽마다 가장 작은 값)
            var m0s = new double[3]; var m1s = new double[3];
            for (int k = 0; k < 40; k++) { Mold(k % 2); Near(); }
            for (int r = 0; r < 3; r++)
            {
                var near0 = new List<double>(); var near1 = new List<double>();
                for (int k = 0; k < 24; k++) { Mold(k % 2); ShipView.Painter.Prof = new(); Near(); (k % 2 == 0 ? near0 : near1).Add(MoldMs()); }
                m0s[r] = Mid(near0); m1s[r] = Mid(near1);
            }
            ShipView.Painter.Prof = null;
            var m0 = new Three(m0s[0], m0s[1], m0s[2]); var m1 = new Three(m1s[0], m1s[1], m1s[2]);
            // 넓은 보기: 곰팡이 0 ↔ 1을 번갈아 두면 곰팡이 단이 바뀐 덩어리를 그 장에 모두 다시 굽는다 (FarBakeMs 0) — 덩어리 하나를 굽는 곰팡이 단계 (합 ÷ 구운 수)의 가운데 값 (첫 장 뒤 12장을 세 번)
            var b0s = new double[3]; var b1s = new double[3];
            double keepMs = ShipView.Painter.FarBakeMs; int keepMax = ShipView.Painter.FarBakeMax;
            ShipView.Painter.FarBakeMs = 0; ShipView.Painter.FarBakeMax = 0;
            try
            {
                var bake0 = new List<double>(); var bake1 = new List<double>();
                for (int k = 0; k <= 36; k++)
                {
                    Mold(k % 2);
                    ShipView.Painter.Prof = new(); int b0 = p.Baked;
                    Far16();
                    int nb = p.Baked - b0;
                    if (k > 0 && nb > 0) (k % 2 == 0 ? bake0 : bake1).Add(MoldMs() / nb);
                    if (k > 0 && k % 12 == 0) { b0s[k / 12 - 1] = Mid(bake0); b1s[k / 12 - 1] = Mid(bake1); bake0.Clear(); bake1.Clear(); }
                }
            }
            finally { ShipView.Painter.Prof = null; ShipView.Painter.FarBakeMs = keepMs; ShipView.Painter.FarBakeMax = keepMax; }
            var b0ne = new Three(b0s[0], b0s[1], b0s[2]); var b1ne = new Three(b1s[0], b1s[1], b1s[2]);
            int rebaked;
            Mold(1);
            // 다 구운 뒤에 잰다 (예산 고침 — 데우기 40장 동안 장마다 FarBakeMs씩 다시 굽던 덩어리 (16도트 창에 87개)가 느린 판에서는 잰 장까지 남아 16도트 한 장이 9.3ms로 보였다): 배 전체 한 장에 바뀐 덩어리를 모두
            ShipView.Painter.FarBakeMs = 0;
            try { int b0 = p.Baked; Far8(); rebaked = p.Baked - b0; }
            finally { ShipView.Painter.FarBakeMs = keepMs; }
            var (mNear, mFar16, mFar8) = Rounds(Med(Near), Med(Far16), Med(Far8));
            if (prof) { StageProf("곰팡이 16도트", Far16); StageProf("곰팡이 8도트", Far8); }
            // 평시 판 + 1ms: 같은 시드 · 같은 틱의 평시 판을 하나 더 띄워 (화가도 따로 — 덩어리를 따로 굽는다) 한 장씩 번갈아 잰다 (몇 초씩 이어지는 다른 일의 흔들림이 두 쪽에 같게 — 맨 처음 잰 평시 판 값은 6.4 · 5.6 · 4.9ms로 흔들렸다)
            var s0 = ShipWorld.Crewed("Saetteul", 9, 40, 16, 4);
            s0.Run(300);
            var p0 = new ShipView.Painter(s0.Layout);
            p0.FrameFar(s0, -1, -1, W + 2, H + 2, 0, 8, into);
            var (c16, g16) = Pair(() => p0.FrameFar(s0, W / 2 - 48, H / 2 - 27, 97, 55, 3, 16, into), Far16);
            var (c8, g8) = Pair(() => p0.FrameFar(s0, -1, -1, W + 2, H + 2, 3, 8, into), Far8);
            Mold(0);
            Check("그리기 예산: 곰팡이 핀 배 (바닥 · 문 칸 모두 곰팡이 1 · 안개도) — 32도트 곰팡이 단계 (바닥 · 벽 · 문 + 칸 덮개) +0.5ms 안 · 한 장 ≤ 6ms · 덩어리 하나 다시 굽기 +0.06ms 안 · 16 · 8도트 ≤ 8ms",
                m1 - m0 <= 0.5 && mNear <= 6 && b1ne - b0ne <= 0.06 && mFar16 <= 8 && mFar8 <= 8,
                $"곰팡이 칸 {moldy.Length} · 단계 {m0:0.00} → {m1:0.00}ms · 덩어리 굽기 {b0ne:0.000} → {b1ne:0.000}ms · 다시 구운 덩어리 {rebaked} · 한 장 {mNear:0.0} · {mFar16:0.0} · {mFar8:0.0}ms");
            Check("그리기 예산: 곰팡이 핀 배 넓은 보기 한 장 — 평시 판 + 1ms 안 (16 · 8도트 — 곰팡이 · 안개가 장마다 드는 몫 · 평시 판과 한 장씩 번갈아)",
                g16 - c16 <= 1 && g8 - c8 <= 1, $"16도트 {c16:0.0} → {g16:0.0}ms · 8도트 {c8:0.0} → {g8:0.0}ms");
        }
        // 쌓아 그리기 (물리 PH2 · docs/PHYSICS.md §6-2): 탁자 (2×2칸 · 62×60도트 · 12층) 한 장 짓기 — 캐시 없이 64단을 세 번 · 단마다 가장 작은 값의 가운데
        // 32도트와 넓은 보기 16도트 (그림 도트는 1/4 · 격자 고르기 — 반 도트씩 옮긴 넷의 발자국을 한 번 찍어 센다 — 를 더해)
        {
            var table = DotForge.Make(Tables.Default, "Table", Facing.S, 2, 2);
            foreach (var (sc, cap) in new[] { (1, 0.2), (2, 0.15) })
            {
                for (int k = 0; k < 64; k++) ShipView.DotStack.Draw(table, k, sc);   // 데우기
                var best = new double[64]; Array.Fill(best, double.MaxValue);
                for (int r = 0; r < 3; r++)
                    for (int k = 0; k < 64; k++) { var sw = Stopwatch.StartNew(); ShipView.DotStack.Draw(table, k, sc); best[k] = Math.Min(best[k], sw.Elapsed.TotalMilliseconds); }
                Array.Sort(best);
                double med = best[32], worst = best[63];
                Check(sc == 1 ? "그리기 예산: 쌓아 그리기 — 탁자 한 장 짓기 ≤ 0.2ms (64단 가운데 값 · 캐시 없이)"
                    : "그리기 예산: 쌓아 그리기 넓은 보기 (16도트) — 탁자 한 장 짓기 ≤ 0.15ms (64단 가운데 값 · 캐시 없이 · 격자 고르기까지)",
                    med <= cap, $"가운데 {med:0.000}ms · 가장 느린 단 {worst:0.000}ms");
            }
        }
    }

    /// <summary>세 번 잰 가운데 값 (그리기 예산): 판정은 가장 작은 값 — 다른 일 · VM 빼앗김은 시간을 더할 뿐 빼지 않는다. 글에는 '고른 값 (셋)'.</summary>
    private readonly record struct Three(double A, double B, double C) : IFormattable
    {
        public double V => Math.Min(A, Math.Min(B, C));
        public static implicit operator double(Three t) => t.V;
        public string ToString(string? format, IFormatProvider? fp) => $"{V.ToString(format, fp)} ({A.ToString(format, fp)}/{B.ToString(format, fp)}/{C.ToString(format, fp)})";
    }

    /// <summary>두 그리기를 한 장씩 번갈아 (데우기 40번 · 장 12쌍의 가운데 값을 세 번 — 쪽마다 가장 작은 값): 다른 일의 흔들림이 두 쪽에 같게.</summary>
    private static (Three a, Three b) Pair(Action fa, Action fb)
    {
        for (int k = 0; k < 40; k++) { fa(); fb(); }
        var ma = new double[3]; var mb = new double[3];
        for (int r = 0; r < 3; r++)
        {
            var la = new List<double>(); var lb = new List<double>();
            for (int k = 0; k < 12; k++)
            {
                var sw = Stopwatch.StartNew(); fa(); la.Add(sw.Elapsed.TotalMilliseconds);
                sw.Restart(); fb(); lb.Add(sw.Elapsed.TotalMilliseconds);
            }
            la.Sort(); lb.Sort(); ma[r] = la[la.Count / 2]; mb[r] = lb[lb.Count / 2];
        }
        return (new Three(ma[0], ma[1], ma[2]), new Three(mb[0], mb[1], mb[2]));
    }

    /// <summary>그리기 예산 줄 셋 (32 · 16 · 8도트 — 데우기 · 한 번 재기)을 함께 잰다: 줄마다 데우기 40장 (단계 번역이 끝나게 — 관전 화면은 1초에 60장)은 한 번 (셋 모두 먼저) ·
    /// 회차마다 줄을 돌아가며 장 12개의 가운데 값 (1회차 셋 → 2회차 셋 → 3회차 셋 — 묶음 시간이 늘지 않게 장 수는 그대로) · 줄마다 세 가운데 값 (판정은 가장 작은 값).
    /// 한 줄의 회차 사이에 다른 두 줄의 재기가 끼어 회차가 시간에 흩어진다 (예산 고침 검토 — 줄마다 데운 뒤 세 회차를 곧바로 이어 재면 0.5초 안에 몰려 데운 직후의 단계 번역 구간이 셋을 다 덮었다:
    /// 첫 줄 32도트 (7.8/10.5/6.0) · (6.8/8.3/3.8)ms — 평소 3.7 ~ 4.1 · 단계 번역을 끄면 3.7 · 4.1 · 5.0).</summary>
    private static (Three a, Three b, Three c) Rounds((Action warm, Func<double> one) a, (Action warm, Func<double> one) b, (Action warm, Func<double> one) c)
    {
        var lines = new[] { a, b, c };
        var m = new double[3, 3];
        foreach (var l in lines) for (int k = 0; k < 40; k++) l.warm();
        for (int r = 0; r < 3; r++)
            for (int i = 0; i < 3; i++) m[i, r] = Mid12(lines[i].one);
        return (new Three(m[0, 0], m[0, 1], m[0, 2]), new Three(m[1, 0], m[1, 1], m[1, 2]), new Three(m[2, 0], m[2, 1], m[2, 2]));
        static double Mid12(Func<double> one)
        {
            var ms = new List<double>();
            for (int k = 0; k < 12; k++) ms.Add(one());
            ms.Sort(); return ms[ms.Count / 2];
        }
    }

    /// <summary>조사용 (DRAW_PROF=1): 그리기 한 장의 단계마다 가운데 값 (데운 뒤 12장 · Painter.Prof).</summary>
    private static void StageProf(string name, Action f)
    {
        for (int k = 0; k < 40; k++) f();
        var per = new Dictionary<string, List<double>>(); var all = new List<double>();
        for (int k = 0; k < 12; k++)
        {
            ShipView.Painter.Prof = new();
            var sw = Stopwatch.StartNew(); f(); all.Add(sw.Elapsed.TotalMilliseconds);
            foreach (var kv in ShipView.Painter.Prof) (per.TryGetValue(kv.Key, out var l) ? l : per[kv.Key] = new()).Add(kv.Value);
        }
        ShipView.Painter.Prof = null;
        static double Mid(List<double> v) { v.Sort(); return v[v.Count / 2]; }
        Console.WriteLine($"    {name} {Mid(all):0.00}ms: " + string.Join(" · ", per.Select(kv => (kv.Key, m: Mid(kv.Value))).Where(x => x.m >= 0.01).OrderByDescending(x => x.m).Select(x => $"{x.Key} {x.m:0.00}")));
    }

    /// <summary>배 하나 · 엔진마다 한 틱 시간 (--profile 배 사람 로봇 [틱]) — 시험이 많이 쓰는 배가 어디에 시간을 쓰나.</summary>
    private static void ProfileShip(string[] args)
    {
        var rest = args.SkipWhile(a => a != "--profile").Skip(1).ToArray();
        string ship = rest.Length > 0 ? rest[0] : "Didim";
        int people = rest.Length > 1 ? int.Parse(rest[1]) : 6, robots = rest.Length > 2 ? int.Parse(rest[2]) : 2, ticks = rest.Length > 3 ? int.Parse(rest[3]) : 3000;
        if (args.Contains("--one")) Par.Threads = 1;
        var make = Stopwatch.StartNew();
        var s = ShipWorld.Crewed(ship, 3, people, robots);
        Console.WriteLine($"  만들기 {make.Elapsed.TotalMilliseconds:0}ms (처음 — 표 읽기 · JIT 함께)");
        Console.WriteLine($"  도면 {s.Layout.W}×{s.Layout.H} = {s.Layout.N}칸 · 바깥 아닌 칸 {Enumerable.Range(0, s.Layout.N).Count(c => s.Layout.Kind[c] != CellKind.Outside)} · 구역 {s.Get<Field>().Regions.Count}");
        if (args.Contains("--make"))   // 배 만들기 단계별 (두 번째부터 — 시험이 배를 만들 때 드는 값)
        {
            for (int k = 0; k < 3; k++)
            {
                var w = Stopwatch.StartNew();
                var lay = Layout.Ship(ship, Tables.Default); double a = w.Elapsed.TotalMilliseconds;
                var sim = new Sim(ShipWorld.Setup(lay, 3)); double b = w.Elapsed.TotalMilliseconds;
                var f = sim.Get<Field>(); f.FillAir(); double c = w.Elapsed.TotalMilliseconds;
                Furnish.Apply(f); double d = w.Elapsed.TotalMilliseconds;
                sim.Get<Life>().Plant(sim.Get<Network>()); double e = w.Elapsed.TotalMilliseconds;
                sim.Get<Mind>().Crew(sim, people, robots, 0); double g = w.Elapsed.TotalMilliseconds;
                sim.Run(1); double h = w.Elapsed.TotalMilliseconds;
                Console.WriteLine($"  만들기 {k + 2}번째 {h:0}ms — 도면 {a:0} · 엔진 짜기 {b - a:0} · 공기 {c - b:0} · 꾸미기 {d - c:0} · 심기 {e - d:0} · 사람 {g - e:0} · 첫 틱 {h - g:0}");
            }
        }
        s.Run(100);
        s.Measure = !args.Contains("--nolap"); s.LastMs.Clear();
        if (args.Contains("--alloc")) { s.Measure = true; s.LastAlloc = new(); Mind.ProfAlloc = new(); }   // 엔진마다 · 마음 안 단계마다 새로 잡는 것 (재기 자체가 느리다)
        bool laps = !args.Contains("--nolap");   // --nolap: 단계별 재기를 끈다 (재기 자체가 25%쯤 먹는다 — tools/hot.sh 표본 수집은 끄고 잰다)
        if (laps) { Field.Prof = new(); Network.Prof = new(); Mind.Prof = new(); Prof.D = new(); }
        long alloc0 = GC.GetTotalAllocatedBytes(true); int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        var pause0 = GC.GetTotalPauseDuration();
        var sw = Stopwatch.StartNew();
        var each = new double[ticks];
        var spikes = new List<(double ms, long tick, string why)>();   // 긴 틱마다 엔진별 몫 (재기를 켰을 때)
        var before = new Dictionary<string, double>();
        for (int k = 0; k < ticks; k++)
        {
            if (s.Measure)
            {
                before.Clear(); foreach (var kv in s.LastMs) before[kv.Key] = kv.Value;
                if (laps) { foreach (var kv in Field.Prof!) before["장:" + kv.Key] = kv.Value; foreach (var kv in Network.Prof!) before["망:" + kv.Key] = kv.Value; foreach (var kv in Mind.Prof!) before["마음:" + kv.Key] = kv.Value; foreach (var kv in Prof.D!) before[kv.Key] = kv.Value; }
            }
            long q = Stopwatch.GetTimestamp();
            s.Step();
            each[k] = (Stopwatch.GetTimestamp() - q) * 1000.0 / Stopwatch.Frequency;
            if (s.Measure && each[k] > 10)
            {
                var now = s.LastMs.Select(kv => (kv.Key, kv.Value)).ToList();
                if (laps) { now.AddRange(Field.Prof!.Select(kv => ("장:" + kv.Key, kv.Value))); now.AddRange(Network.Prof!.Select(kv => ("망:" + kv.Key, kv.Value))); now.AddRange(Mind.Prof!.Select(kv => ("마음:" + kv.Key, kv.Value))); now.AddRange(Prof.D!.Select(kv => (kv.Key, kv.Value))); }
                var d = now.Select(x => (x.Item1, d: x.Item2 - before.GetValueOrDefault(x.Item1))).Where(x => x.d > 0.5).OrderByDescending(x => x.d).ToList();
                spikes.Add((each[k], s.Tick - 1, string.Join(" · ", d.Where(x => !x.Item1.Contains(':')).Take(3).Select(x => $"{x.Item1} {x.d:0.0}")) + " | " + string.Join(" · ", d.Where(x => x.Item1.Contains(':')).Take(4).Select(x => $"{x.Item1} {x.d:0.0}"))));
            }
        }
        double ms = sw.Elapsed.TotalMilliseconds / ticks;
        var sorted = each.OrderBy(x => x).ToArray();
        Console.WriteLine($"  틱 시간: 가운데 {sorted[ticks / 2]:0.00}ms · 99% {sorted[(int)(ticks * 0.99)]:0.00}ms · 가장 김 {sorted[^1]:0.0}ms · 10ms 넘은 틱 {each.Count(x => x > 10)}");
        foreach (var p in spikes.OrderByDescending(x => x.ms).Take(6)) Console.WriteLine($"    {p.ms,5:0.0}ms 틱 {p.tick} ({Time.Clock(p.tick)}) — {p.why}");
        Console.WriteLine($"  할당 틱마다 {(GC.GetTotalAllocatedBytes(true) - alloc0) / (double)ticks / 1024:0.0}KB · 수거 {GC.CollectionCount(0) - gc0}/{GC.CollectionCount(1) - gc1}/{GC.CollectionCount(2) - gc2} · 멈춤 {(GC.GetTotalPauseDuration() - pause0).TotalMilliseconds / ticks:0.000}ms/틱");
        if (laps) {
        Console.WriteLine("  장 안: " + string.Join(" · ", Field.Prof.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / ticks:0.000}")));
        Console.WriteLine("  망 안: " + string.Join(" · ", Network.Prof.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / ticks:0.000}")));
        Console.WriteLine("  마음 안: " + string.Join(" · ", Mind.Prof.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / ticks:0.000}")));
        foreach (var g in Prof.D.GroupBy(kv => kv.Key.Split(':')[0]))
            Console.WriteLine($"  {g.Key} 안: " + string.Join(" · ", g.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key.Split(':')[1]} {kv.Value / ticks:0.000}")));
        }
        Field.Prof = null; Network.Prof = null; Mind.Prof = null; Prof.D = null;
        Console.WriteLine($"{ship} 사람 {people} · 로봇 {robots} · 코어 {Par.Threads}: 틱마다 {ms:0.000}ms (하루 = {ms * Time.TicksPerDay / 1000:0.0}초)");
        if (args.Contains("--clone"))   // 복제 한 번에 드는 것 (연출 미리 보기 · 주컴퓨터 상상)
        {
            for (int k = 0; k < 3; k++)
            {
                long m0 = GC.GetAllocatedBytesForCurrentThread(); var w = Stopwatch.StartNew();
                var blank = new Sim(s.Setup); long m1 = GC.GetAllocatedBytesForCurrentThread(); double t1 = w.Elapsed.TotalMilliseconds;
                var bytes = s.Save(); long m2 = GC.GetAllocatedBytesForCurrentThread(); double t2 = w.Elapsed.TotalMilliseconds;
                blank.Load(bytes); long m3 = GC.GetAllocatedBytesForCurrentThread(); double t3 = w.Elapsed.TotalMilliseconds;
                {
                    var parts = new List<string>();
                    long a0 = GC.GetAllocatedBytesForCurrentThread(); var pw = Stopwatch.StartNew();
                    void P(string nm) { long a1 = GC.GetAllocatedBytesForCurrentThread(); parts.Add($"{nm} {(a1 - a0) / 1048576.0:0.0}MB {pw.Elapsed.TotalMilliseconds:0.0}ms"); a0 = a1; pw.Restart(); }
                    var e = new Sim(new SimSetup(s.Layout, 3, _ => { })); P("빈 세계");
                    var ff = new Field(e.Layout, e.Tables); P("장"); var ch = new Chemistry(ff); P("화학"); var so = new Sound(ff); P("소리"); var li = new Light(ff); P("빛");
                    var stt = new Structure(ff); P("구조"); var ou = new Outside(ff, 3); P("바깥"); var ne = new Network(ff, 3); P("망"); var se = new Sensors(e.Tables); P("감지");
                    var lf = new Life(ff); P("생명"); var mi = new Mind(ff); P("마음"); var soc = new Society(ff.L); P("사회"); var sm = new ShipMind(ff); P("주컴퓨터"); var wk = new Work(ff.L, ff.T); P("일");
                    Console.WriteLine("  새 세계 엔진마다: " + string.Join(" · ", parts));
                }
                long c0 = GC.GetAllocatedBytesForCurrentThread(); var cw = Stopwatch.StartNew();
                s.Clone();
                Console.WriteLine($"  복제 {k + 1}: Clone() {(GC.GetAllocatedBytesForCurrentThread() - c0) / 1048576.0:0.0}MB {cw.Elapsed.TotalMilliseconds:0}ms");
                Console.WriteLine($"  복제 {k + 1}: 새 세계 {(m1 - m0) / 1048576.0:0.0}MB {t1:0}ms · 찍기 {(m2 - m1) / 1048576.0:0.0}MB {t2 - t1:0}ms (크기 {bytes.Length / 1048576.0:0.0}MB) · 읽기 {(m3 - m2) / 1048576.0:0.0}MB {t3 - t2:0}ms");
            }
        }
        var pm = s.Get<Mind>();
        Console.WriteLine($"  길찾기: 부름 {pm.PathCalls} · 못 찾음 {pm.PathFails} · 펼친 칸 {pm.PathExpanded} (부름마다 {pm.PathExpanded / Math.Max(1, pm.PathCalls)})");
        Console.WriteLine("  " + string.Join(" · ", s.LastMs.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / ticks:0.000}")));
        if (Mind.ProfAlloc != null) { Console.WriteLine("  마음 안 할당 KB/틱: " + string.Join(" · ", Mind.ProfAlloc.OrderByDescending(kv => kv.Value).Take(12).Select(kv => $"{kv.Key} {kv.Value / 1024.0 / ticks:0.0}"))); Mind.ProfAlloc = null; }
        if (s.LastAlloc != null) Console.WriteLine("  할당 엔진마다 KB/틱: " + string.Join(" · ", s.LastAlloc.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / 1024.0 / ticks:0.0}")));
    }

    private static void Bench()
    {
        Console.WriteLine("성능\n");
        var l = Worlds.Ascii(BigShip(18, 12)); // 약 163×85 = 1.4만 칸 · 방 216
        foreach (int threads in new[] { 1, Math.Max(2, Environment.ProcessorCount) })
        {
            Par.Threads = threads;
            var s = Worlds.FieldOnly(l, 1, f =>
            {
                Stir(f);
                for (int d = 0; d < f.L.Doors.Count; d += 3) f.SetDoor(d, 1f);
                f.SetHole(Worlds.At(f.L, 0, 3), 0.05f);
            });
            s.Run(20);
            Field.Prof = new();
            var sw = Stopwatch.StartNew();
            const int Ticks = 500;
            s.Run(Ticks);
            double ms = sw.Elapsed.TotalMilliseconds / Ticks;
            var f = s.Get<Field>();
            Console.WriteLine("    " + string.Join(" · ", Field.Prof.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / Ticks:0.000}")));
            Field.Prof = null;
            Console.WriteLine($"  장(기체 · 열 · 물) {f.Cells}칸 · 구역 {f.Regions.Count} · 틈 {f.Links.Count} · 코어 {threads}: 틱마다 {ms:0.000}ms (게임 1시간 = {ms * Time.TicksPerHour / 1000:0.00}초)");
        }
        // 평시: 구멍 없음 · 문 몇 개 열림 · 100명이 숨 쉰다 (틱마다 산소를 빼고 이산화탄소 · 수증기를 넣는다)
        foreach (int threads in new[] { 1, Math.Max(2, Environment.ProcessorCount) })
        {
            Par.Threads = threads;
            var s = Worlds.FieldOnly(l, 1, f => { for (int d = 0; d < f.L.Doors.Count; d += 3) f.SetDoor(d, 1f); });
            var f = s.Get<Field>();
            var floor = Enumerable.Range(0, f.Cells).Where(i => f.L.Kind[i] == CellKind.Floor).ToArray();
            var rng = Rng.Stream(1, "bench");
            var people = Enumerable.Range(0, 100).Select(_ => floor[rng.Range(0, floor.Length)]).ToArray();
            void Breathe()
            {
                foreach (int c in people)
                {
                    f.RemoveGas(c, f.T.gO2, 0.0006);          // 약 0.25 L/분 × 2.4초
                    f.AddGas(c, f.T.gCO2, 0.0005, 307);
                    f.AddGas(c, f.T.gH2O, 0.0004, 307);
                    f.AddHeat(c, 100 * Time.SecondsPerTick); // 사람 100W
                }
            }
            for (int i = 0; i < 20; i++) { Breathe(); s.Step(); }
            Field.Prof = new();
            var sw = Stopwatch.StartNew();
            const int Ticks = 1000;
            for (int i = 0; i < Ticks; i++) { Breathe(); s.Step(); }
            double ms = sw.Elapsed.TotalMilliseconds / Ticks;
            Console.WriteLine("    " + string.Join(" · ", Field.Prof.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / Ticks:0.000}")));
            Field.Prof = null;
            Console.WriteLine($"  평시 100명 숨 · {f.Cells}칸 · 코어 {threads}: 틱마다 {ms:0.000}ms (게임 1시간 = {ms * Time.TicksPerHour / 1000:0.00}초)");
        }
        // 100명 · 로봇 30이 탄 큰 배(한누리호): 마음 엔진 · 전체
        {
            Par.Threads = Math.Max(2, Environment.ProcessorCount);
            var s = ShipWorld.Crewed("hannuri", 1, 100, 30);
            s.Run(50);
            s.Measure = true;
            s.LastMs.Clear();
            var sw = Stopwatch.StartNew();
            const int Ticks = 1500;
            s.Run(Ticks);
            double ms = sw.Elapsed.TotalMilliseconds / Ticks;
            var m = s.Get<Mind>();
            Console.WriteLine("    " + string.Join(" · ", s.LastMs.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value / Ticks:0.000}")));
            var sm = s.Get<ShipMind>();
            if (Environment.GetEnvironmentVariable("BENCH_LOG") == "1") foreach (var (t, txt) in sm.Log) Console.WriteLine($"    {Time.Clock(t)} {txt}");
            Console.WriteLine($"  한누리호 주컴퓨터: 사건 {sm.Incidents.Count(x => x.Active != 0)} · 상상 {sm.Imaginations.Count / 2}번 · 경보 {sm.Alarms} · 맡김 {sm.Assignments}");
            Console.WriteLine($"  한누리호 사람 {m.Agents.Count(a => a.IsPerson)} · 로봇 {m.Agents.Count(a => !a.IsPerson)} · 코어 {Par.Threads}: 틱마다 {ms:0.000}ms (마음 {s.LastMs.GetValueOrDefault("마음") / Ticks:0.000}ms) · 게임 1시간 = {ms * Time.TicksPerHour / 1000:0.00}초");
        }
        Par.Threads = Math.Max(1, Environment.ProcessorCount);
    }

    private static void Ships()
    {
        foreach (var key in Layout.ShipKeys())
        {
            var l = Layout.Ship(key, Tables.Default);
            Console.WriteLine($"  {l.Key} {l.Name} — {l.W}×{l.H} · 방 {l.Rooms.Count} · 문 {l.Doors.Count} · 설비 {l.Furniture.Count}");
        }
    }
}
