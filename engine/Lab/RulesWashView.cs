using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>
/// 화장실 그림 (5단 A1 — docs/stage5/view.md): 샤워 칸은 쓰는 동안 커튼이 닫히고 (그 사람의 걸음을 그린다 — 커튼은 세계 값이 아니다) · 물이 흐르면 물줄기 ·
/// 변기는 때 · 막힘 · '사용 금지' 불을 읽고 · 설비 칸에 고인 물은 발치에 웅덩이로 · 세면대는 손 씻는 동안 물줄기.
/// 그림은 세계를 읽기만 한다 — 모두 기준 그림과의 차로 센다 (커튼 윗단 · 도기가 이미 밝기 230을 넘는다).
/// </summary>
public static partial class Program
{
    // 화장실 시험 배(WashShip)에 물 재생기 w 하나 — 샤워 칸 · 세면대에 물관이 깔려 끊을 수 있다 (WashShip은 기존 시험이 쓰므로 그대로 둔다)
    private const string WashViewShip = @"
##################
#e.....#T......T.#
#......D.........#
#pbw...#.S....k..#
##################";

    private static void WashViewRules()
    {
        if (!Do("화장실 그림")) return;
        Console.WriteLine("\n[화장실 그림 — 샤워 칸 커튼 · 물줄기 · 변기 때 · 막힘 · 사용 금지 불 · 발치 물 · 세면대 물줄기 (기준 그림과의 차)]");
        var legend = new Dictionary<char, string> { ['e'] = "Bed", ['T'] = "Toilet", ['S'] = "Shower", ['k'] = "Sink", ['p'] = "PowerPanel", ['b'] = "Battery", ['w'] = "WaterRecycler" };
        var lay = Worlds.Ascii(WashViewShip, legend);
        var s = Worlds.Full(lay, 3);
        s.Run(6);   // 빛이 한 번 잰다 (5틱마다)
        var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>(); var f = s.Get<Field>();
        var p = new Painter(lay) { Lighting = false };   // 빛깔만 견준다
        int W = lay.W, H = lay.H, T = Painter.T;
        double bakeMs = Painter.FarBakeMs; int bakeMax = Painter.FarBakeMax;
        Painter.FarBakeMs = 0; Painter.FarBakeMax = 1;   // 넓은 보기는 한 장에 덩어리 하나 (같은 세계 같은 그림)
        Img Shot() => p.Frame(s, 0, 0, W, H, 0);
        Img Far() => p.FrameFar(s, 0, 0, W, H, 0, 16);
        // 설비 몸 그림의 자리 (x0..x1 · y0..y1 — 32도트 몸 그림 좌표: 위로 솟은 rise만큼 칸 위에서 시작) 안에서 고른 점 수 · D는 칸당 도트
        int Box(Img img, FurnitureDef fu, int rise, int x0, int y0, int x1, int y1, Func<Rgba, bool> pick, int D = 32)
        {
            int bx = lay.X(fu.Cells[0]) * D, by = lay.Y(fu.Cells[0]) * D - rise * D / T, n = 0;
            for (int y = y0 * D / T; y < y1 * D / T; y++)
                for (int x = x0 * D / T; x < x1 * D / T; x++)
                    if (pick(img.Get(bx + x, by + y))) n++;
            return n;
        }
        bool Same(Img a, Img b, FurnitureDef fu, int rise, int x0, int y0, int x1, int y1)
        {
            int bx = lay.X(fu.Cells[0]) * T, by = lay.Y(fu.Cells[0]) * T - rise;
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) if (!a.Get(bx + x, by + y).Equals(b.Get(bx + x, by + y))) return false;
            return true;
        }
        static bool Teal(Rgba q) => q.G > q.R + 60 && q.B > q.R + 60;                   // 커튼 (청록 주름)
        static bool Spray(Rgba q) => q.B >= 240 && q.B - q.R >= 30 && q.G >= 200;       // 흐르는 물 (밝은 물빛 — 타일 · 도기 · 커튼 윗단은 아니다)
        static bool Brown(Rgba q) => q.R > q.G + 10 && q.G > q.B + 10 && q.R >= 70;     // 때 · 막힌 물
        static bool Red(Rgba q) => q.R > 200 && q.G < 90;                               // '사용 금지' 불
        static bool Pool(Rgba q) => q.B > q.R + 35 && q.B >= 120;                      // 고인 물

        // ── 샤워 칸: 쓰는 동안 커튼이 닫힌다 (막대 위로 머리만) · 물이 흐르면 물줄기 · 넓은 보기도 ──
        {
            var shower = lay.Furniture.First(x => x.Type == "Shower");
            int sc = shower.Cells[0], dev = net.Devices.First(d => d.Furniture == shower.Id).Id;
            const int SR = 14;   // 샤워 칸이 솟은 도트
            var e0 = Shot(); var ef0 = Far();
            int n0 = Box(e0, shower, SR, 0, 0, T, T + SR, Teal), w0 = Box(e0, shower, SR, 0, 0, T, T + SR, Spray);
            int nf0 = Box(ef0, shower, SR, 0, 0, T, T + SR, Teal, 16), wf0 = Box(ef0, shower, SR, 0, 0, T, T + SR, Spray, 16);
            int pp = m.AddPerson(s, sc);
            ref var c = ref m[pp].C;
            m[pp].Plan.Clear(); c.StepIndex = 0; c.Goal = GoalKind.Wash;
            var seen = Shot();   // 샤워 칸에 서 있기만 한 이 (들어서는 이 — 커튼은 접힌 채)
            m[pp].Plan.Add(new PlanStep { Kind = StepKind.Use, Arg = (byte)UseKind.Shower, Value = Time.Minutes(6), Cell = sc, Target = shower.Id });
            c.StepIndex = 0; c.StepStart = s.Tick;
            var shut = Shot(); var shutFar = Far();
            int n1 = Box(shut, shower, SR, 0, 0, T, T + SR, Teal), nf1 = Box(shutFar, shower, SR, 0, 0, T, T + SR, Teal, 16);
            // 사람 도트 (서 있기만 한 그림에서 빈 칸과 다른 점): 막대(17줄) 아래 · 커튼 밑단(43줄) 위에 그대로 남은 점 · 막대 위에 그대로 남은 점
            int under = 0, over = 0, bx = lay.X(sc) * T, by = lay.Y(sc) * T - SR;
            for (int y = 0; y < T + SR; y++)
                for (int x = 0; x < T; x++)
                {
                    var q = seen.Get(bx + x, by + y);
                    if (q.Equals(e0.Get(bx + x, by + y)) || !q.Equals(shut.Get(bx + x, by + y))) continue;
                    if (y >= 19 && y < 43) under++; else if (y < 17) over++;
                }
            Check("화장실 그림: 샤워 칸 — 그 칸에서 [샤워] 걸음인 이가 있으면 커튼이 닫힌다 (청록 주름이 늘고 · 막대 아래 몸은 가리고 · 막대 위로 머리만)",
                n1 >= n0 + 250 && under == 0 && over > 0, $"청록 {n0} → {n1} · 막대 아래 보이는 몸 {under} · 막대 위 머리 {over}");
            // 사람을 빼고 밸브만 연다 (틀어 둔 채 나간 샤워 칸)
            c.Cell = lay.Index(2, 2); life[c.Body].Cell = c.Cell; m[pp].Plan.Clear(); c.StepIndex = 0;
            net.SetValve(dev, true);
            var open = Shot(); var openFar = Far();
            int w1 = Box(open, shower, SR, 0, 0, T, T + SR, Spray), n2 = Box(open, shower, SR, 0, 0, T, T + SR, Teal), wf1 = Box(openFar, shower, SR, 0, 0, T, T + SR, Spray, 16);
            net.SetValve(dev, false);
            var closed = Shot();
            int w2 = Box(closed, shower, SR, 0, 0, T, T + SR, Spray);
            Check("화장실 그림: 샤워 칸 — 밸브를 열면 (사람 없이) 물줄기가 흐르고 커튼은 접힌 채 · 잠그면 그친다",
                w1 >= w0 + 40 && n2 == n0 && w2 == w0 && Same(closed, e0, shower, SR, 0, 0, T, T + SR), $"물빛 {w0} → {w1} → {w2} · 청록 {n0} → {n2}");
            Check("화장실 그림: 넓은 보기 16도트도 — 쓰는 동안 닫힌 커튼 · 흐르는 물줄기",
                nf1 > nf0 && wf1 > wf0, $"청록 {nf0} → {nf1} · 물빛 {wf0} → {wf1}");
            // 앞 가림: 샤워 칸 바로 뒤(위 칸)에 선 이는 솟은 뒷벽(14도트)에 다리가 가리고 머리는 보인다
            c.Cell = sc - lay.W; life[c.Body].Cell = c.Cell;
            var behind = Shot();
            bool hidden = Same(behind, closed, shower, SR, 1, 0, T - 1, SR);
            int head = 0;
            for (int y = 0; y < T - SR; y++) for (int x = 0; x < T; x++) if (!behind.Get(lay.X(sc) * T + x, (lay.Y(sc) - 1) * T + y).Equals(closed.Get(lay.X(sc) * T + x, (lay.Y(sc) - 1) * T + y))) head++;
            c.Cell = lay.Index(2, 2); life[c.Body].Cell = c.Cell;
            Check("화장실 그림: 앞 가림 — 샤워 칸 바로 뒤에 선 이는 솟은 뒷벽에 다리가 가리고 머리는 보인다",
                hidden && head > 20, $"뒷벽 그대로 {hidden} · 보이는 윗몸 {head}");
        }

        // ── 변기: 때 · 막힘 · '사용 금지' 불 — 닦고 · 뚫고 · 불을 끄면 처음 그림으로 · 넓은 보기는 다시 굽는다 ──
        {
            var toilet = lay.Furniture.First(x => x.Type == "Toilet");
            int fu = toilet.Id;
            const int TR = 6;
            life.Scrub(fu);   // 변기 배열을 잡는다 (처음 쓸 때까지 빈 배열)
            var t0 = Shot(); var tf0 = Far();
            int Ring(Img im) => Box(im, toilet, TR, 0, 0, T, T + TR, Brown);
            int Bowl(Img im) => Box(im, toilet, TR, 10, 16, 22, 30, Brown);
            int Lamp(Img im, int D = 32) => Box(im, toilet, TR, 0, 0, T, T + TR, Red, D);
            int b0 = Ring(t0), k0 = Bowl(t0), r0 = Lamp(t0), bf0 = Box(tf0, toilet, TR, 0, 0, T, T + TR, Brown, 16), rf0 = Lamp(tf0, 16);
            life.Grime[fu] = 0.9f;
            var tg = Shot();
            Img tgf = Far(); for (int n = 0; n < 3; n++) tgf = Far();   // 바뀐 덩어리를 다시 굽는다 (한 장에 하나)
            int bg = Ring(tg), bgf = Box(tgf, toilet, TR, 0, 0, T, T + TR, Brown, 16);
            life.Scrub(fu);
            life.Clog(fu, s.Causes.Add(CauseKind.Setup, "시험: 변기가 막혔다", s.Tick, toilet.Cells[0])); life.Bowl[fu] = 0.3;
            var tk = Shot();
            int bk = Bowl(tk);
            life.Unclog(fu);
            m.SignToilet(fu, true);
            var tl = Shot(); var tlf = Far();
            int rl = Lamp(tl), rlf = Lamp(tlf, 16);
            m.SignToilet(fu, false);
            var tb = Shot();
            bool back = Same(tb, t0, toilet, TR, 0, 0, T, T + TR);
            Check("화장실 그림: 변기 — 때(0.9)는 대야 둘레 갈색 · 막힘(걸린 대변 0.3kg)은 대야에 탁한 물 · '사용 금지' 불은 빨간 점 · 닦고 · 뚫고 · 불을 끄면 처음 그림",
                bg >= b0 + 15 && bk >= k0 + 25 && rl >= r0 + 4 && back && life.Clogged[fu] == 0,
                $"갈색 {b0} → {bg} · 대야 안 갈색 {k0} → {bk} · 빨강 {r0} → {rl} · 처음 그림 {back}");
            Check("화장실 그림: 넓은 보기 16도트도 — 때가 끼면 다시 구운 덩어리에 갈색 · 불은 장마다 빨강",
                bgf > bf0 && rlf > rf0, $"갈색 {bf0} → {bgf} · 빨강 {rf0} → {rlf}");
        }

        // ── 설비 칸의 고인 물: 변기를 덮지 않고 발치(칸 22~31줄)에 웅덩이 ──
        {
            var toilet = lay.Furniture.First(x => x.Type == "Toilet");
            int tc = toilet.Cells[0];
            const int TR = 6;
            var dry = Shot();
            f.AddStore(tc, f.T.sWater, 30);
            var wet = Shot();
            bool bowl = Same(wet, dry, toilet, TR, 13, 14 + TR, 19, 20 + TR);   // 대야 6×6 (칸 14~19줄)
            int pool0 = Box(dry, toilet, TR, 0, 22 + TR, T, 32 + TR, Pool), pool1 = Box(wet, toilet, TR, 0, 22 + TR, T, 32 + TR, Pool);
            f.TakeStore(tc, f.T.sWater, f.Stored(f.T.sWater, tc));
            Check("화장실 그림: 변기 칸에 고인 물(30몰)은 변기를 덮지 않고 발치에 웅덩이 — 대야는 그대로 · 칸 아래쪽에 물빛",
                bowl && pool1 >= pool0 + 30, $"대야 그대로 {bowl} · 발치 물빛 {pool0} → {pool1}");
        }

        // ── 세면대: 손 씻는 걸음인 이(곁 칸에 선다)가 있고 물이 오면 물줄기 · 물관이 끊기면 없다 ──
        {
            var sink = lay.Furniture.First(x => x.Type == "Sink");
            int kc = sink.Cells[0], side = lay.Index(lay.X(kc) - 1, lay.Y(kc));
            const int KR = 10;
            int hp = m.AddPerson(s, side);
            ref var c = ref m[hp].C;
            m[hp].Plan.Clear(); c.StepIndex = 0; c.Goal = GoalKind.Toilet;
            var k0 = Shot();
            m[hp].Plan.Add(new PlanStep { Kind = StepKind.Use, Arg = (byte)UseKind.Hands, Value = Time.Minutes(0.5), Cell = kc, Target = sink.Id });
            c.StepIndex = 0; c.StepStart = s.Tick;
            var k1 = Shot();
            bool user = net.WaterUser(sink.Id);
            net.CutLines(s, kc, Network.Bit(LineKind.Water), s.Causes.Add(CauseKind.Setup, "시험: 세면대 밑 물관이 끊겼다", s.Tick, kc));
            var k2 = Shot();
            int a0 = Box(k0, sink, KR, 10, 14, 23, 28, Spray), a1 = Box(k1, sink, KR, 10, 14, 23, 28, Spray), a2 = Box(k2, sink, KR, 10, 14, 23, 28, Spray);
            Check("화장실 그림: 세면대 — 손 씻는 걸음인 이가 곁에 서 있고 물이 오면 수도꼭지에서 대야로 물줄기 · 물관이 끊기면 없다",
                user && !net.WaterAt(sink.Id) && a1 >= a0 + 6 && a2 == a0, $"물관 {user} · 물빛 {a0} → {a1} · 끊김 {a2}");
        }
        Painter.FarBakeMs = bakeMs; Painter.FarBakeMax = bakeMax;
    }
}
