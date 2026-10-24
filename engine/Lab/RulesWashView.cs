using ShipEngine;
using ShipSim.Art;
using ShipView;
using ShipView.Audio;

namespace Lab;

/// <summary>
/// 화장실 그림 (5단 A1 — docs/stage5/view.md): 샤워 칸은 쓰는 동안 커튼이 닫히고 (그 사람의 걸음을 그린다 — 커튼은 세계 값이 아니다) · 물이 흐르면 물줄기 ·
/// 변기는 때 · 막힘 · '사용 금지' 불을 읽고 · 설비 칸에 고인 물은 발치에 웅덩이로 (많이 고이면 설비가 물에 선다) · 세면대는 손 씻는 동안 물줄기.
/// 그림은 세계를 읽기만 한다 — 모두 기준 그림과의 차로 센다 (커튼 윗단 · 도기가 이미 밝기 230을 넘는다).
/// 빛깔은 빛 끈 그림으로 견주고, 게임이 쓰는 빛 켠 그림 · 넓은 보기(16 · 8도트)에도 같은 것이 나오는지 본다 (A1 검토: 그 길들에 시험이 없었다).
/// 5단 A2: 김 (뜨거운 물이 흐르면 샤워기 위로 — 찬물은 없다) · 습도 0.9 넘는 바닥의 흰 안개 · 세면대 칸 0.85 넘으면 거울 김 서림 (세계 습도를 읽기만 — 0.4는 그대로).
/// A2 검토: 넓은 보기 · 빛 켠 그림의 거울 김 서림 · 습도 문턱 바로 아래 (0.85 · 0.8) · 습할수록 많은 김 · 안개 네 점씩 = 한 점씩 · 벽 바로 아래 샤워 칸의 김이 벽을 넘지 않는다 (SteamWallShip).
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

    // 벽 바로 아래 샤워 칸 둘 (A2 검토 — 김이 벽을 넘던 것): 위 방 S는 선체 벽 아래 (두 칸 위는 바깥) · 아래 방 S는 안쪽 벽 아래 (두 칸 위는 위 방 바닥)
    private const string SteamWallShip = @"
##########
#S.......#
#........#
####D#####
#pbw..S..#
#........#
##########";

    private static void WashViewRules()
    {
        if (!Do("화장실 그림")) return;
        Console.WriteLine("\n[화장실 그림 — 샤워 칸 커튼 · 물줄기 · 변기 때 · 막힘 · 사용 금지 불 · 발치 물 · 세면대 물줄기 · 김 · 안개 · 거울 김 서림 · 벽을 넘지 않는 김 (기준 그림과의 차)]");
        var legend = new Dictionary<char, string> { ['e'] = "Bed", ['T'] = "Toilet", ['S'] = "Shower", ['k'] = "Sink", ['p'] = "PowerPanel", ['b'] = "Battery", ['w'] = "WaterRecycler" };
        var lay = Worlds.Ascii(WashViewShip, legend);
        var s = Worlds.Full(lay, 3);
        s.Run(6);   // 빛이 한 번 잰다 (5틱마다)
        var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>(); var f = s.Get<Field>();
        var p = new Painter(lay) { Lighting = false };   // 빛깔만 견준다
        var pl = new Painter(lay);   // 게임이 쓰는 길 (빛 켬 — Painter.Lighting 기본값): 스스로 빛나는 것은 빛을 곱한 뒤에 그린다
        int W = lay.W, H = lay.H, T = Painter.T;
        double bakeMs = Painter.FarBakeMs; int bakeMax = Painter.FarBakeMax;
        Painter.FarBakeMs = 0; Painter.FarBakeMax = 1;   // 넓은 보기는 한 장에 덩어리 하나 (같은 세계 같은 그림)
        Img Shot() => p.Frame(s, 0, 0, W, H, 0);
        Img Far() => p.FrameFar(s, 0, 0, W, H, 0, 16);
        Img Lit() => pl.Frame(s, 0, 0, W, H, 0);
        Img LitFar(int D) => pl.FrameFar(s, 0, 0, W, H, 0, D);
        // 설비 몸 그림의 자리 (x0..x1 · y0..y1 — 32도트 몸 그림 좌표: 위로 솟은 rise만큼 칸 위에서 시작) 안에서 고른 점 수 · D는 칸당 도트
        int Box(Img img, FurnitureDef fu, int rise, int x0, int y0, int x1, int y1, Func<Rgba, bool> pick, int D = 32)
        {
            int bx = lay.X(fu.Cells[0]) * D, by = lay.Y(fu.Cells[0]) * D - rise * D / T, n = 0;
            for (int y = y0 * D / T; y < y1 * D / T; y++)
                for (int x = x0 * D / T; x < x1 * D / T; x++)
                    if (pick(img.Get(bx + x, by + y))) n++;
            return n;
        }
        bool Same(Img a, Img b, FurnitureDef fu, int rise, int x0, int y0, int x1, int y1, int D = 32)
        {
            int bx = lay.X(fu.Cells[0]) * D, by = lay.Y(fu.Cells[0]) * D - rise * D / T;
            for (int y = y0 * D / T; y < y1 * D / T; y++) for (int x = x0 * D / T; x < x1 * D / T; x++) if (!a.Get(bx + x, by + y).Equals(b.Get(bx + x, by + y))) return false;
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
            var seen = Shot(); var seenFar = Far();   // 샤워 칸에 서 있기만 한 이 (들어서는 이 — 커튼은 접힌 채)
            m[pp].Plan.Add(new PlanStep { Kind = StepKind.Use, Arg = (byte)UseKind.Shower, Value = Time.Minutes(6), Cell = sc, Target = shower.Id });
            c.StepIndex = 0; c.StepStart = s.Tick;
            var shut = Shot(); var shutFar = Far();
            int n1 = Box(shut, shower, SR, 0, 0, T, T + SR, Teal), nf1 = Box(shutFar, shower, SR, 0, 0, T, T + SR, Teal, 16);
            // 사람 도트 (서 있기만 한 그림 seen에서 빈 칸 e와 다른 점) 가운데 커튼 친 그림 shut에도 그대로 남은 점 — 샤워 칸 몸 그림 y0..y1줄 (사람: 머리 12~22 · 목 23~25 · 몸 26~44줄)
            int Visible(Img e, Img sn, Img sh, int y0, int y1, int D = 32)
            {
                int bx = lay.X(sc) * D, by = lay.Y(sc) * D - SR * D / T, n = 0;
                for (int y = y0 * D / T; y < y1 * D / T; y++)
                    for (int x = 0; x < D; x++)
                    {
                        var q = sn.Get(bx + x, by + y);
                        if (!q.Equals(e.Get(bx + x, by + y)) && q.Equals(sh.Get(bx + x, by + y))) n++;
                    }
                return n;
            }
            int face = Visible(e0, seen, shut, 19, 23), under = Visible(e0, seen, shut, 26, 44), faceF = Visible(ef0, seenFar, shutFar, 18, 24, 16);
            Check("화장실 그림: 샤워 칸 — 그 칸에서 [샤워] 걸음인 이가 있으면 커튼이 닫힌다 (청록 주름이 늘고 · 목 아래 몸은 가리고 · 커튼 위로 얼굴까지 머리가 보인다)",
                n1 >= n0 + 250 && under == 0 && face >= 30, $"청록 {n0} → {n1} · 커튼 위 얼굴(19~22줄) {face} · 가린 몸(26~43줄)에 보이는 점 {under}");
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
            Check("화장실 그림: 넓은 보기 16도트도 — 쓰는 동안 닫힌 커튼 (얼굴은 커튼 위에) · 흐르는 물줄기",
                nf1 > nf0 && faceF >= 6 && wf1 > wf0, $"청록 {nf0} → {nf1} · 얼굴 {faceF} · 물빛 {wf0} → {wf1}");
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
            var lit0 = Lit(); var litF0 = LitFar(16); var lit80 = LitFar(8);
            m.SignToilet(fu, true);
            var tl = Shot(); var tlf = Far();
            var lit1 = Lit(); var litF1 = LitFar(16); var lit81 = LitFar(8);
            int rl = Lamp(tl), rlf = Lamp(tlf, 16);
            m.SignToilet(fu, false);
            var tb = Shot();
            var lit2 = Lit(); var litF2 = LitFar(16); var lit82 = LitFar(8);
            bool back = Same(tb, t0, toilet, TR, 0, 0, T, T + TR);
            int l0 = Lamp(lit0), l1 = Lamp(lit1), lf0 = Lamp(litF0, 16), lf1 = Lamp(litF1, 16), l80 = Lamp(lit80, 8), l81 = Lamp(lit81, 8);
            bool litBack = Same(lit2, lit0, toilet, TR, 0, 0, T, T + TR) && Same(litF2, litF0, toilet, TR, 0, 0, T, T + TR, 16) && Same(lit82, lit80, toilet, TR, 0, 0, T, T + TR, 8);
            Check("화장실 그림: 변기 — 때(0.9)는 대야 둘레 갈색 · 막힘(걸린 대변 0.3kg)은 대야에 탁한 물 · '사용 금지' 불은 빨간 점 · 닦고 · 뚫고 · 불을 끄면 처음 그림",
                bg >= b0 + 15 && bk >= k0 + 25 && rl >= r0 + 4 && back && life.Clogged[fu] == 0,
                $"갈색 {b0} → {bg} · 대야 안 갈색 {k0} → {bk} · 빨강 {r0} → {rl} · 처음 그림 {back}");
            Check("화장실 그림: 넓은 보기 16도트도 — 때가 끼면 다시 구운 덩어리에 갈색 · 불은 장마다 빨강",
                bgf > bf0 && rlf > rf0, $"갈색 {bf0} → {bgf} · 빨강 {rf0} → {rlf}");
            Check("화장실 그림: 빛을 켠 그림 (게임이 쓰는 길) — '사용 금지' 불은 32 · 16 · 8도트 모두 빨간 점 (빛을 곱한 뒤 스스로 빛난다) · 끄면 처음 그림",
                l1 >= l0 + 4 && lf1 > lf0 && l81 > l80 && litBack, $"빨강 32도트 {l0} → {l1} · 16도트 {lf0} → {lf1} · 8도트 {l80} → {l81} · 끄면 처음 그림 {litBack}");
        }

        // ── 설비 칸의 고인 물: 변기를 덮지 않고 발치(칸 22~31줄)에 웅덩이 ──
        {
            var toilet = lay.Furniture.First(x => x.Type == "Toilet");
            int tc = toilet.Cells[0];
            const int TR = 6;
            var dry = Shot(); var dryF = Far();
            f.AddStore(tc, f.T.sWater, 30);
            var wet = Shot(); var wetF = Far();
            bool bowl = Same(wet, dry, toilet, TR, 13, 14 + TR, 19, 20 + TR), bowlF = Same(wetF, dryF, toilet, TR, 13, 14 + TR, 19, 20 + TR, 16);   // 대야 6×6 (칸 14~19줄)
            int pool0 = Box(dry, toilet, TR, 0, 22 + TR, T, 32 + TR, Pool), pool1 = Box(wet, toilet, TR, 0, 22 + TR, T, 32 + TR, Pool);
            int poolF0 = Box(dryF, toilet, TR, 0, 22 + TR, T, 32 + TR, Pool, 16), poolF1 = Box(wetF, toilet, TR, 0, 22 + TR, T, 32 + TR, Pool, 16);
            f.TakeStore(tc, f.T.sWater, f.Stored(f.T.sWater, tc));
            Check("화장실 그림: 변기 칸에 고인 물(30몰)은 변기를 덮지 않고 발치에 웅덩이 — 대야는 그대로 · 칸 아래쪽에 물빛",
                bowl && pool1 >= pool0 + 30, $"대야 그대로 {bowl} · 발치 물빛 {pool0} → {pool1}");
            Check("화장실 그림: 넓은 보기 16도트도 — 변기 칸에 고인 물(30몰)은 발치에 · 대야는 마른 그림 그대로",
                bowlF && poolF1 >= poolF0 + 8, $"대야 그대로 {bowlF} · 발치 물빛 {poolF0} → {poolF1}");
            // 물이 많이 고였다 (물관이 터지거나 넘친 물이 쌓였다 — 400몰): 곁 바닥 칸처럼 칸에 물이 차고 변기는 그 물에 서 있다 (발치의 작은 타원만 남은 마른 섬이 아니다)
            int nb = tc + 1;   // 변기 오른쪽 빈 바닥 칸
            f.AddStore(tc, f.T.sWater, 400); f.AddStore(nb, f.T.sWater, 400);
            var fl = Shot(); var flF = Far();
            bool bowl4 = Same(fl, dry, toilet, TR, 13, 14 + TR, 19, 20 + TR), bowl4F = Same(flF, dryF, toilet, TR, 13, 14 + TR, 19, 20 + TR, 16);
            // 물빛: 마른 그림보다 푸른 점 (칸 y0..y1줄 · 칸 폭 전체 — 파랑 − 빨강이 20 넘게 는 점: 물 타일은 어두운 바닥에서 파랑 120에 못 미친다)
            int Wetter(Img a, Img b, int cell, int y0, int y1, int D = 32)
            {
                int bx = lay.X(cell) * D, by = lay.Y(cell) * D, n = 0;
                for (int y = y0 * D / T; y < y1 * D / T; y++)
                    for (int x = 0; x < D; x++)
                    {
                        Rgba q = a.Get(bx + x, by + y), q0 = b.Get(bx + x, by + y);
                        if ((q.B - q.R) - (q0.B - q0.R) >= 20) n++;
                    }
                return n;
            }
            int ft = Wetter(fl, dry, tc, 29, 32), fn = Wetter(fl, dry, nb, 29, 32), ftF = Wetter(flF, dryF, tc, 29, 32, 16), fnF = Wetter(flF, dryF, nb, 29, 32, 16);
            int bt = Wetter(fl, dry, tc, 22, 32), bn = Wetter(fl, dry, nb, 22, 32);
            f.TakeStore(tc, f.T.sWater, f.Stored(f.T.sWater, tc)); f.TakeStore(nb, f.T.sWater, f.Stored(f.T.sWater, nb));
            Check("화장실 그림: 변기 칸에 물이 많이 고이면(400몰) 곁 바닥 칸처럼 물이 차고 변기는 그 물에 서 있다 — 발치 아래 줄(29~31)의 물빛이 곁 칸과 비슷 · 대야는 그대로 (32 · 16도트)",
                fn >= 80 && ft * 10 >= fn * 9 && fnF >= 20 && ftF * 10 >= fnF * 9 && bowl4 && bowl4F,
                $"29~31줄 물빛 변기 칸 {ft} · 곁 칸 {fn} (16도트 {ftF} · {fnF}) · 22~31줄 {bt} · {bn} · 대야 그대로 {bowl4} · {bowl4F}");
        }

        // ── 세면대: 손 씻는 걸음인 이(곁 칸에 선다)가 있고 물이 오면 물줄기 · 물관이 끊기면 없다 ──
        {
            var sink = lay.Furniture.First(x => x.Type == "Sink");
            int kc = sink.Cells[0], side = lay.Index(lay.X(kc) - 1, lay.Y(kc));
            const int KR = 10;
            int hp = m.AddPerson(s, side);
            ref var c = ref m[hp].C;
            m[hp].Plan.Clear(); c.StepIndex = 0; c.Goal = GoalKind.Toilet;
            var k0 = Shot(); var k0F = Far();
            m[hp].Plan.Add(new PlanStep { Kind = StepKind.Use, Arg = (byte)UseKind.Hands, Value = Time.Minutes(0.5), Cell = kc, Target = sink.Id });
            c.StepIndex = 0; c.StepStart = s.Tick;
            var k1 = Shot(); var k1F = Far();
            bool user = net.WaterUser(sink.Id);
            net.CutLines(s, kc, Network.Bit(LineKind.Water), s.Causes.Add(CauseKind.Setup, "시험: 세면대 밑 물관이 끊겼다", s.Tick, kc));
            var k2 = Shot(); var k2F = Far();
            int a0 = Box(k0, sink, KR, 10, 14, 23, 28, Spray), a1 = Box(k1, sink, KR, 10, 14, 23, 28, Spray), a2 = Box(k2, sink, KR, 10, 14, 23, 28, Spray);
            Check("화장실 그림: 세면대 — 손 씻는 걸음인 이가 곁에 서 있고 물이 오면 수도꼭지에서 대야로 물줄기 · 물관이 끊기면 없다",
                user && !net.WaterAt(sink.Id) && a1 >= a0 + 6 && a2 == a0, $"물관 {user} · 물빛 {a0} → {a1} · 끊김 {a2}");
            // 넓은 보기 16도트: 줄인 물줄기는 대야 빛깔과 섞인다 — 기준 그림보다 푸른 점(파랑 − 빨강이 는 점)으로 센다
            int Bluer(Img a, Img b)
            {
                int bx = lay.X(kc) * 16 + 10 / 2, by = lay.Y(kc) * 16 - KR / 2, n = 0;
                for (int y = 14 / 2; y < 28 / 2; y++)
                    for (int x = 0; x < (23 - 10) / 2; x++)
                    {
                        Rgba q = a.Get(bx + x, by + y), q0 = b.Get(bx + x, by + y);
                        if (q.B - q.R > q0.B - q0.R) n++;
                    }
                return n;
            }
            int fb1 = Bluer(k1F, k0F);
            bool cutBack = Same(k2F, k0F, sink, KR, 10, 14, 23, 28, 16);
            Check("화장실 그림: 넓은 보기 16도트도 — 손 씻는 동안 수도꼭지 아래 물빛이 늘고 · 물관이 끊기면 기준 그림 그대로",
                fb1 >= 2 && cutBack, $"기준 그림보다 푸른 점 {fb1} · 끊기면 그대로 {cutBack}");
        }

        // ── 김 · 습한 방의 안개 · 거울 김 서림 (5단 A2): 뜨거운 물이 흐르면 샤워기 위로 흰 김 · 습도 0.9 넘는 바닥 칸은 흰 막 · 세면대 칸이 0.85를 넘으면 거울이 뿌옇다 (세계 습도를 읽기만 · 장마다) ──
        {
            var shower = lay.Furniture.First(x => x.Type == "Shower");
            var sink = lay.Furniture.First(x => x.Type == "Sink");
            int sc = shower.Cells[0], kc = sink.Cells[0], dev = net.Devices.First(d => d.Furniture == shower.Id).Id;
            const int SR = 14, KR = 10;
            var room = lay.Rooms.First(r => r.Cells.Contains(sc));
            var free = room.Cells.Where(c => lay.Kind[c] == CellKind.Floor && lay.FurnitureAt[c] < 0).ToArray();   // 가구 없는 바닥 칸
            static int Lum(Rgba q) => (q.R + q.G + q.B) / 3;
            static bool White(Rgba q) => q.R > 230 && q.G > 230 && q.B > 230;   // 흰 점 (물줄기 · 물빛 타일은 붉은 기가 230에 못 미친다)
            // 샤워기 위 띠: 샤워 칸과 양옆 (3칸 폭) · 샤워 칸 위 두 칸부터 샤워기(몸 그림 10줄 — 칸 위 4도트) 바로 위까지 — 물줄기(12줄부터)는 8도트로 줄여도 들지 않는다
            (int xa, int ya, int xb, int yb) BandOf(int D) => ((lay.X(sc) - 1) * D, (lay.Y(sc) - 2) * D, (lay.X(sc) + 2) * D, (lay.Y(sc) * T - (SR - 10) - 1) * D / T);
            int Band(Img im, Func<Rgba, bool> pick)
            {
                var (xa, ya, xb, yb) = BandOf(32);
                int n = 0;
                for (int y = ya; y < yb; y++) for (int x = xa; x < xb; x++) if (pick(im.Get(x, y))) n++;
                return n;
            }
            int Brighter(Img a, Img b, int D)   // 띠 안에서 기준 그림보다 20 넘게 밝아진 점
            {
                var (xa, ya, xb, yb) = BandOf(D);
                int n = 0;
                for (int y = ya; y < yb; y++) for (int x = xa; x < xb; x++) if (Lum(a.Get(x, y)) - Lum(b.Get(x, y)) >= 20) n++;
                return n;
            }
            double FloorMean(Img im, int D)   // 가구 없는 바닥 칸의 밝기 평균
            {
                long sum = 0, n = 0;
                foreach (int c in free)
                    for (int y = 0; y < D; y++) for (int x = 0; x < D; x++) { sum += Lum(im.Get(lay.X(c) * D + x, lay.Y(c) * D + y)); n++; }
                return (double)sum / Math.Max(1, n);
            }
            bool SameCells(Img a, Img b, IEnumerable<int> cells, int D)
            {
                foreach (int c in cells)
                    for (int y = 0; y < D; y++) for (int x = 0; x < D; x++) if (!a.Get(lay.X(c) * D + x, lay.Y(c) * D + y).Equals(b.Get(lay.X(c) * D + x, lay.Y(c) * D + y))) return false;
                return true;
            }
            bool SameRoom(Img a, Img b, int D) => SameCells(a, b, room.Cells, D);
            // 방 칸 (cells를 주면 그 칸만)의 수증기를 상대 습도 rh로 (RulesCracks 응력 부식의 식 — 칸 온도로)
            void Humid(double rh, int[]? cells = null)
            {
                foreach (int c in cells ?? room.Cells)
                {
                    double t = f.Temp(c), want = rh * Water.SatKPa(t) * 1000 * Field.CellVolume / (Field.R * t), have = f.Gas(f.T.gH2O, c);
                    if (want > have) f.AddGas(c, f.T.gH2O, want - have, t); else f.RemoveGas(c, f.T.gH2O, have - want);
                }
            }
            double rh0 = f.RelHumidity(sc);
            // (a) 사람 없이 밸브와 온수기를 켜고 3틱 (전기가 들어 온수기가 돈다 — 주컴퓨터가 빈 샤워 칸을 잠그는 2분보다 짧다)
            net.SetValve(dev, true); net.SetOn(dev, true);
            s.Run(3);
            bool hot = net.ShowerRunning(dev) && net.Working(dev);
            var hotI = Shot(); var hotF = Far(); var hot8 = p.FrameFar(s, 0, 0, W, H, 0, 8); var hotL = Lit();
            net.SetValve(dev, false);   // 밸브를 닫은 기준 (같은 틱 — 그림만)
            var offI = Shot(); var offF = Far(); var off8 = p.FrameFar(s, 0, 0, W, H, 0, 8); var offL = Lit();
            // (b) 온수기를 끈 찬물: 물줄기는 흐르고 김은 없다
            net.SetValve(dev, true); net.SetOn(dev, false);
            bool cold = net.ShowerRunning(dev) && !net.Working(dev);
            var coldI = Shot(); var coldF = Far();
            net.SetValve(dev, false);
            int wh = Band(hotI, White), w0 = Band(offI, White), wc = Band(coldI, White);
            int sp0 = Box(offI, shower, SR, 0, 0, T, T + SR, Spray), spc = Box(coldI, shower, SR, 0, 0, T, T + SR, Spray);
            Check("화장실 그림: 김 — 밸브와 온수기를 켜고 3틱 (사람 없이) 샤워기 위로 흰 김이 오른다 · 온수기를 끈 찬물은 김 없이 물줄기만",
                hot && cold && wh > w0 + 20 && wc == w0 && spc >= sp0 + 40, $"습도 {rh0:0.00} · 더운 물 {hot} · 흰 점 잠김 {w0} → 더운 물 {wh} · 찬물 {wc} · 찬물 물줄기 물빛 {sp0} → {spc}");
            int bf = Brighter(hotF, offF, 16), b8 = Brighter(hot8, off8, 8), bl = Brighter(hotL, offL, 32), bc = Brighter(coldF, offF, 16);
            Check("화장실 그림: 김 — 빛 켠 그림 (게임이 쓰는 길) · 넓은 보기 16 · 8도트도 샤워기 위가 밝아진다 · 찬물은 그대로",
                bl >= 20 && bf >= 6 && b8 >= 2 && bc == 0, $"밝아진 점 빛 켬 {bl} · 16도트 {bf} · 8도트 {b8} · 찬물 16도트 {bc}");
            // (c) 방 칸을 습도 0.97로: 바닥에 흰 막 · 거울이 뿌옇다 · (d) 0.4: 그대로
            int Mirror(Img im) => Box(im, sink, KR, 7, 1, 25, 10, q => Lum(q) > 230);
            var dry = Shot(); var dryF = Far(); var dry8 = p.FrameFar(s, 0, 0, W, H, 0, 8); var dryL = Lit(); var dryLF = LitFar(16);
            Humid(0.97);
            double rhW = f.RelHumidity(kc);
            var wet = Shot(); var wetF = Far(); var wet8 = p.FrameFar(s, 0, 0, W, H, 0, 8); var wetL = Lit();
            Humid(0.4);
            double rhL = f.RelHumidity(kc);
            var low = Shot(); var lowF = Far(); var low8 = p.FrameFar(s, 0, 0, W, H, 0, 8);
            Humid(rh0);
            double m0 = FloorMean(dry, 32), m1 = FloorMean(wet, 32);
            int g0 = Mirror(dry), g1 = Mirror(wet);
            bool lowSame = SameRoom(low, dry, 32) && SameRoom(lowF, dryF, 16) && SameRoom(low8, dry8, 8);
            Check("화장실 그림: 습한 방 (습도 0.97) — 바닥에 흰 안개 (가구 없는 바닥 칸 밝기 평균 +8 넘게) · 세면대 거울이 뿌옇다 · 습도 0.4는 그대로 (32 · 16 · 8도트)",
                rhW > 0.95 && rhL < 0.42 && m1 > m0 + 8 && g1 > g0 + 10 && lowSame,
                $"습도 {rhW:0.00} · 바닥 밝기 {m0:0.0} → {m1:0.0} · 거울 밝은 점 {g0} → {g1} · 습도 {rhL:0.00} 그대로 {lowSame}");
            double f0 = FloorMean(dryF, 16), f1 = FloorMean(wetF, 16), e0 = FloorMean(dry8, 8), e1 = FloorMean(wet8, 8), l0 = FloorMean(dryL, 32), l1 = FloorMean(wetL, 32);
            Check("화장실 그림: 습한 방 — 빛 켠 그림 · 넓은 보기 16 · 8도트도 바닥에 흰 안개",
                l1 > l0 + 4 && f1 > f0 + 8 && e1 > e0 + 8, $"바닥 밝기 빛 켬 {l0:0.0} → {l1:0.0} · 16도트 {f0:0.0} → {f1:0.0} · 8도트 {e0:0.0} → {e1:0.0}");

            // ── A2 검토 고침: 거울 김 서림의 넓은 보기 · 빛 켠 그림 갈래 · 습도 문턱 · 습할수록 많고 짙은 김 (그 갈래를 틀리게 하면 빨강) ──
            // (e) 세면대 칸만 습도 0.97: 거울 사각(몸 그림 1~9줄)은 위 칸에 걸쳐 바닥 안개가 덮지 않는다 — 기준 그림보다 20 넘게 밝아진 거울 점 (32 · 16 · 8도트 · 빛 켬 32 · 16도트)
            int Misted(Img a, Img b, int D = 32)
            {
                int bx = lay.X(kc) * D, by = lay.Y(kc) * D - KR * D / T, n = 0;
                for (int y = 1 * D / T; y < 10 * D / T; y++) for (int x = 7 * D / T; x < 25 * D / T; x++) if (Lum(a.Get(bx + x, by + y)) - Lum(b.Get(bx + x, by + y)) >= 20) n++;
                return n;
            }
            Humid(0.97, new[] { kc });
            var mi32 = Shot(); var mi16 = Far(); var mi8 = p.FrameFar(s, 0, 0, W, H, 0, 8); var miL = Lit(); var miLF = LitFar(16);
            Humid(rh0);
            int g32 = Misted(mi32, dry), g16 = Misted(mi16, dryF, 16), g8 = Misted(mi8, dry8, 8), gL = Misted(miL, dryL), gLF = Misted(miLF, dryLF, 16);
            Check("화장실 그림: 거울 김 서림 — 넓은 보기 16 · 8도트와 빛 켠 그림 (32 · 16도트)도 세면대 칸 습도 0.97이면 거울이 뿌옇다 (세면대 칸만 — 바닥 안개는 거울에 없다)",
                g32 >= 40 && g16 >= 10 && g8 >= 3 && gL >= 40 && gLF >= 10, $"밝아진 거울 점 32도트 {g32} · 16도트 {g16} · 8도트 {g8} · 빛 켬 {gL} · 빛 켬 16도트 {gLF}");
            // (f) 문턱 바로 아래: 습도 0.85 — 바닥 칸은 처음 그림 그대로 (안개는 0.9부터 · 거울이 걸친 칸은 뺀다) · 0.8 — 거울까지 방 그림이 그대로 (김 서림은 0.85부터)
            var bare = free.Where(c => c != kc - lay.W).ToArray();
            Humid(0.85);
            double rh85 = f.RelHumidity(sc);
            var u85 = Shot(); var u85F = Far(); var u858 = p.FrameFar(s, 0, 0, W, H, 0, 8);
            Humid(0.80);
            double rh80 = f.RelHumidity(kc);
            var u80 = Shot(); var u80F = Far(); var u808 = p.FrameFar(s, 0, 0, W, H, 0, 8);
            Humid(rh0);
            bool floor85 = SameCells(u85, dry, bare, 32) && SameCells(u85F, dryF, bare, 16) && SameCells(u858, dry8, bare, 8);
            bool room80 = SameRoom(u80, dry, 32) && SameRoom(u80F, dryF, 16) && SameRoom(u808, dry8, 8);
            Check("화장실 그림: 습도 문턱 — 0.85에서는 바닥에 안개가 없고 (0.9부터) · 0.8에서는 거울까지 방 그림이 처음 그대로 (김 서림은 0.85부터 · 32 · 16 · 8도트)",
                rh85 is > 0.83 and < 0.87 && rh80 is > 0.78 and < 0.82 && floor85 && room80, $"습도 {rh85:0.000} 바닥 그대로 {floor85} · 습도 {rh80:0.000} 방 그대로 {room80}");
            // (g) 습할수록 많고 짙은 김: 같은 틱 · 같은 방에서 더운 물 그림과 찬물 그림의 차 (샤워기 위 띠에서 20 넘게 밝아진 점) — 방 습도 0.95와 처음 습도
            int SteamGain(double rh)
            {
                Humid(rh);
                net.SetValve(dev, true); net.SetOn(dev, true);
                var a = Shot();
                net.SetOn(dev, false);
                var b = Shot();
                net.SetValve(dev, false);
                return Brighter(a, b, 32);
            }
            int s95 = SteamGain(0.95), s50 = SteamGain(rh0);
            Check("화장실 그림: 김 — 습한 방(0.95)의 김은 처음 습도 방보다 많고 짙다 (샤워기 위에서 밝아진 점 — 같은 틱 · 찬물 그림과의 차)",
                s95 >= s50 * 3 / 2 + 20, $"밝아진 점 습도 {rh0:0.00} {s50} → 0.95 {s95}");
            // (h) 안개의 네 점씩 섞기 (정수 — 빠른 길)가 한 점씩 섞기와 같은 그림 (Painter.NoVector · 습도 0.97 · 32 · 16 · 8도트)
            Humid(0.97);
            var v32 = Shot(); var v16 = Far(); var v8 = p.FrameFar(s, 0, 0, W, H, 0, 8);
            Painter.NoVector = true;
            var o32 = Shot(); var o16 = Far(); var o8 = p.FrameFar(s, 0, 0, W, H, 0, 8);
            Painter.NoVector = false;
            Humid(rh0);
            bool sameVec = SameRoom(v32, o32, 32) && SameRoom(v16, o16, 16) && SameRoom(v8, o8, 8);
            Check("화장실 그림: 안개 — 네 점씩 섞은 그림이 한 점씩 섞은 그림과 같다 (습도 0.97 · 32 · 16 · 8도트)",
                sameVec && FloorMean(v32, 32) > m0 + 8, $"같다 {sameVec} · 바닥 밝기 {m0:0.0} → {FloorMean(v32, 32):0.0}");
        }

        // ── 벽 너머로 새지 않는 김 (A2 검토): 선체 벽 바로 아래 샤워 칸 (두 칸 위는 바깥) · 안쪽 벽 바로 아래 샤워 칸 (두 칸 위는 옆방 바닥) ──
        // 김은 그 방의 공기에만 — 그 방 바닥과 그 방을 마주한 벽 앞면 위에 · 벽 뚜껑 · 바깥 · 옆방은 김을 켜도 찬물 그림 그대로 (32 · 16 · 8도트 · 빛 켬) ·
        // 천장 (위 벽 앞면 위쪽 줄)에 닿은 김은 앞으로 굴러 나와 샤워 칸 위에 보인다
        {
            var wlay = Worlds.Ascii(SteamWallShip, legend);
            var w = Worlds.Full(wlay, 3);
            w.Run(6);
            var wn = w.Get<Network>();
            var wp = new Painter(wlay) { Lighting = false };
            var wpl = new Painter(wlay);
            int WW = wlay.W, WH = wlay.H;
            string Leak(FurnitureDef fu, string where)
            {
                int dv = wn.Devices.First(d => d.Furniture == fu.Id).Id, sx = wlay.X(fu.Cells[0]), sy = wlay.Y(fu.Cells[0]);
                wn.SetValve(dv, true); wn.SetOn(dv, true);
                w.Run(3);
                bool hot = wn.ShowerRunning(dv) && wn.Working(dv);
                Img[] Shots() => new[] { wp.Frame(w, 0, 0, WW, WH, 0), wp.FrameFar(w, 0, 0, WW, WH, 0, 16), wp.FrameFar(w, 0, 0, WW, WH, 0, 8), wpl.Frame(w, 0, 0, WW, WH, 0) };
                var on = Shots();
                wn.SetOn(dv, false);   // 같은 틱 · 찬물 (김만 없다)
                var off = Shots();
                wn.SetValve(dv, false);
                // 샤워 칸과 양옆 칸 (3칸 폭) — 두 칸 위 줄 전체 · 바로 위 벽 칸의 뚜껑(위 18도트): 바뀐 점 / 바로 위 벽 앞면(아래 14도트)과 샤워 칸 위 12도트: 바뀐 점 (김이 보인다)
                int Diff(Img a, Img b, int D, int ya, int yb)
                {
                    int n = 0;
                    for (int y = ya * D / T; y < yb * D / T; y++) for (int x = (sx - 1) * D; x < (sx + 2) * D; x++) if (!a.Get(x, y).Equals(b.Get(x, y))) n++;
                    return n;
                }
                int[] Ds = { 32, 16, 8, 32 };
                var leak = Enumerable.Range(0, 4).Select(i => Diff(on[i], off[i], Ds[i], (sy - 2) * T, (sy - 1) * T + ArtWalls.CapH)).ToArray();
                var seen = Enumerable.Range(0, 4).Select(i => Diff(on[i], off[i], Ds[i], (sy - 1) * T + ArtWalls.CapH, sy * T + 12)).ToArray();
                bool ok = hot && leak.All(n => n == 0) && seen[0] >= 130 && seen[1] >= 40 && seen[2] >= 12 && seen[3] >= 130;   // 천장에 닿은 김은 앞으로 굴러 나와 방 안에 보인다 (자르기만 하면 100 · 29 · 12)
                return $"{(ok ? "" : "✘ ")}{where} 더운 물 {hot} · 벽 너머 바뀐 점 {string.Join("/", leak)} · 방 안 김 {string.Join("/", seen)}";
            }
            var showers = wlay.Furniture.Where(x => x.Type == "Shower").OrderBy(x => x.Cells[0]).ToArray();
            string hull = Leak(showers[0], "선체 벽 아래"), inner = Leak(showers[1], "안쪽 벽 아래");
            Check("화장실 그림: 김은 벽을 넘지 않는다 — 선체 벽 · 안쪽 벽 바로 아래 샤워 칸에서 김을 켜도 벽 뚜껑 · 바깥 · 옆방 바닥은 찬물 그림 그대로 · 천장에 닿은 김은 앞으로 굴러 나와 방 안(벽 앞면 · 샤워 칸 위)에 보인다 (32 · 16 · 8도트 · 빛 켬)",
                !hull.StartsWith("✘") && !inner.StartsWith("✘"), $"{hull} | {inner} (32 · 16 · 8 · 빛 켬)");
        }
        Painter.FarBakeMs = bakeMs; Painter.FarBakeMax = bakeMax;
    }

    /// <summary>
    /// 화장실 소리 (5단 A4 — 화면 몫 · docs/stage5/view.md I): 이어 도는 소리의 열쇠가 겹치지 않는다 (있던 결함 — 사람 i의 스피커 음악과 장치 4i+1의 기계 소리가 같은 열쇠라
    /// 섞개가 먼저 만든 기계 목소리에 음악을 먹였다) · 물이 흐르는 샤워 칸은 기계 웅웅이 아니라 쏴아 (물줄기 · 투둑 · 배수구) — 그 칸에 사람이 서 있으면 몸이 물을 받아 먹먹하다 · 잠그면 그친다.
    /// 귀는 세계를 읽기만 한다 (세계의 샤워 칸 소리 크기는 C7에서 물 흐름을 따른다 — 지금은 온수기가 돌 때 표의 50dB).
    /// </summary>
    private static void WashSoundRules()
    {
        if (!Do("화장실 소리")) return;
        Console.WriteLine("\n[화장실 소리 — 이어 도는 소리 열쇠 (음악 · 기계) · 샤워 물 쏴아 (기계 웅웅이 아니다) · 사람이 서면 먹먹 · 잠그면 그친다]");
        var legend = new Dictionary<char, string> { ['e'] = "Bed", ['T'] = "Toilet", ['S'] = "Shower", ['k'] = "Sink", ['p'] = "PowerPanel", ['b'] = "Battery", ['w'] = "WaterRecycler" };
        var lay = Worlds.Ascii(WashViewShip, legend);
        var show = new LiveShow(Worlds.Full(lay, 3), "시험호");
        var s = show.Sim;
        var m = s.Get<Mind>(); var net = s.Get<Network>(); var life = s.Get<Life>();
        const double dt = 1.0 / 60;
        show.Advance(dt);
        show.Ears.Captions = true;
        SoundFrame Ear(int ear)   // 그 칸에서 한 장 듣는다 (세계는 나아가지 않는다 — 귀만)
        {
            show.Ears.ForceEar = ear;
            while (show.Ears.Out.TryDequeue(out _)) { }
            show.Ears.Remap(ear);
            show.Ears.Update(show, dt);
            var fr = new SoundFrame();
            while (show.Ears.Out.TryDequeue(out var f)) fr = f;
            return fr;
        }

        // ── 열쇠: 사람 i가 스피커를 틀고 장치 4i+1(변기 — 가만히 있어도 1m 40dB)이 들린다 → 두 이어 도는 소리의 열쇠가 다르고 · 섞개가 소리마다 목소리를 따로 둔다 ──
        {
            var toilet = net.Devices.First(d => d.Type == "Toilet" && d.Id % 4 == 1);
            int who = (toilet.Id - 1) / 4, at = toilet.Cell + lay.W;   // 변기 바로 아래 칸
            while (m.Agents.Count <= who) m.AddPerson(s, at);
            ref var c = ref m[who].C;
            c.Cell = at; life[c.Body].Cell = at;
            c.Playing = 1; c.PlayedAt = s.Tick;   // 스피커 (시험 차리기 — 트는 것은 '음악' 시험이 본다)
            var fr = Ear(at);
            c.Playing = 0;
            var tune = fr.Loops.Where(u => u.Kind == LoopKind.Tune).ToList();
            var hum = fr.Loops.Where(u => u.Kind == LoopKind.Machine).ToList();
            bool distinct = fr.Loops.Select(u => u.Key).Distinct().Count() == fr.Loops.Count;
            var mix = new Mixer { MusicGain = 0 };
            mix.Apply(fr);
            int voices = mix.Voices - fr.Shots.Count;
            Check("화장실 소리: 이어 도는 소리 열쇠 — 사람 i가 스피커를 틀고 장치 4i+1(변기 웅웅)이 들리면 두 열쇠가 다르고 · 섞개가 소리마다 목소리를 따로 둔다 (있던 결함: 같은 열쇠라 음악이 기계 목소리에 먹혔다)",
                m[who].Id == who && m[who].IsPerson && tune.Count == 1 && hum.Count >= 1 && distinct && voices == fr.Loops.Count,
                $"사람 {who} · 장치 {toilet.Id} · 음악 열쇠 {string.Join("/", tune.Select(u => u.Key))} · 기계 열쇠 {string.Join("/", hum.Select(u => u.Key))} · 이어 도는 소리 {fr.Loops.Count} · 섞개 목소리 {voices}");
        }

        // ── 샤워 칸: 밸브 · 온수기를 켜 두면 (사람 없이 3틱) 쏴아 하나 (열쇠 5,000,000 + 장치) · 그 장치의 기계 웅웅은 없다 · 자막 '쏴아 — 샤워 물' ·
        //    그 칸에 사람이 서면 B = 1 (몸이 물을 받는다) · 잠그면 같은 틱 (온수기는 아직 돈다)에도 쏴아가 없다 · 흐름 스위치가 온수기를 끈 뒤에도 없다 ──
        {
            var shower = lay.Furniture.First(x => x.Type == "Shower");
            int sc = shower.Cells[0], dev = net.Devices.First(d => d.Furniture == shower.Id).Id, ear = sc + 1;
            LoopUpdate[] Water(SoundFrame f) => f.Loops.Where(u => u.Kind == LoopKind.Water).ToArray();
            bool HumOf(SoundFrame f) => f.Loops.Any(u => u.Kind == LoopKind.Machine && (u.Key == 3_000_000 + dev || u.Key == 6_000_000 + dev));
            var quiet = Ear(ear);
            net.SetValve(dev, true); net.SetOn(dev, true);
            s.Run(3);
            bool hot = net.ShowerRunning(dev) && net.Working(dev);
            var on = Ear(ear);
            var caps = show.Ears.CaptionsNow();
            int pp = m.AddPerson(s, sc);
            var stand = Ear(ear);
            m[pp].C.Cell = lay.Index(lay.X(sc), lay.Y(sc) - 1); life[m[pp].C.Body].Cell = m[pp].C.Cell;   // 샤워 칸 밖으로
            net.SetValve(dev, false);
            bool heater = net.NoiseDb(dev) > 30;   // 온수기는 아직 돈다 (흐름 스위치는 다음 틱에 끈다)
            var shut = Ear(ear);
            s.Run(2);
            var after = Ear(ear);
            var w = Water(on);
            Check("화장실 소리: 샤워 칸 — 밸브와 온수기를 켜 두면 (사람 없이) 이어 도는 소리에 샤워 물 하나 (열쇠 5,000,000 + 장치) · 그 장치의 기계 웅웅은 없다 · 자막 '쏴아 — 샤워 물' · 켜기 전엔 없다",
                hot && Water(quiet).Length == 0 && w.Length == 1 && w[0].Key == 5_000_000 + dev && w[0].B == 0 && w[0].Gain > 0 && !HumOf(on) && caps.Any(x => x.Text == "쏴아 — 샤워 물"),
                $"더운 물 {hot} · 켜기 전 {Water(quiet).Length} · 샤워 물 {string.Join("/", w.Select(u => $"열쇠 {u.Key} B {u.B:0} 크기 {u.Gain:0.000}"))} · 기계 웅웅 {HumOf(on)} · 자막 {string.Join(" / ", caps.Select(x => x.Text))}");
            var ws = Water(stand);
            Check("화장실 소리: 샤워 칸 — 그 칸에 사람이 서면 B = 1 (몸이 물을 받아 먹먹하다) · 밸브를 잠그면 같은 틱 (온수기는 아직 돈다)에도 쏴아가 없고 · 온수기가 꺼진 뒤에도 없다",
                ws.Length == 1 && ws[0].B == 1 && heater && Water(shut).Length == 0 && Water(after).Length == 0 && !HumOf(after),
                $"사람이 선 샤워 물 {string.Join("/", ws.Select(u => $"B {u.B:0}"))} · 잠근 틱 온수기 {heater} · 쏴아 {Water(shut).Length} · 두 틱 뒤 {Water(after).Length} · 기계 {HumOf(after)}");
        }

        // ── 섞개 · 목소리: 샤워 물 루프는 쏴아 목소리로 울리고 · 사람이 서면 5kHz 위가 반 아래로 준다 · 목록에서 빠지면 그친다 ──
        {
            var mix = new Mixer { MusicGain = 0 };
            var fr = new SoundFrame();
            fr.Loops.Add(new LoopUpdate { Key = 5_000_000, Kind = LoopKind.Water, A = 1, B = 0, Gain = 1, Cutoff = 16000 });
            var buf = new float[Dsp.Rate * 2];
            mix.Apply(fr); mix.Render(buf, Dsp.Rate);   // 1초 (크기가 따라 오른다)
            double rms = Math.Sqrt(buf.Skip(Dsp.Rate).Sum(x => (double)x * x) / Dsp.Rate);   // 뒤 0.5초
            OnePole m1 = default, m2 = default;
            double mHi = 0, mAll = 0;
            for (int i = 0; i < Dsp.Rate; i++) { float x = buf[2 * i], y = m2.High(m1.High(x, 2000), 2000); if (i >= Dsp.Rate / 2) { mHi += y * y; mAll += x * x; } }   // 왼쪽 · 뒤 0.5초 — 2kHz 위 몫 (이 거름으로 흰 잡음 0.45 · 방 공기 웅웅 0)
            double hiShare = mHi / Math.Max(1e-12, mAll);
            mix.Apply(new SoundFrame());
            mix.Render(buf, Dsp.Rate); mix.Render(buf, Dsp.Rate);
            int left = mix.Voices;
            (double hi, double all) Play(float person)
            {
                var v = new Spray(5);
                v.Set(1, person);
                for (int i = 0; i < Dsp.Rate; i++) v.Next();   // 따라 오를 때까지
                OnePole h1 = default, h2 = default;
                double e = 0, a = 0;
                for (int i = 0; i < Dsp.Rate; i++) { float x = v.Next(), y = h2.High(h1.High(x, 5000), 5000); e += y * y; a += x * x; }
                return (e / Dsp.Rate, Math.Sqrt(a / Dsp.Rate));
            }
            var open = Play(0); var body = Play(1);
            Check("화장실 소리(섞개 · 목소리): 샤워 물 루프는 쏴아 목소리로 울리고 (흰 잡음 1~9kHz · 투둑 · 배수구) · 사람이 서면 5kHz 위가 반 아래로 준다 · 목록에서 빠지면 그친다",
                rms > 0.02 && hiShare > 0.2 && left == 0 && open.all > 0.1 && body.hi < open.hi * 0.5 && body.all > open.all * 0.4,
                $"섞개 RMS {rms:0.000} · 2kHz 위 몫 {hiShare:0.00} · 빠진 뒤 목소리 {left} · 목소리 RMS {open.all:0.000} → 사람 {body.all:0.000} · 5kHz 위 {10 * Math.Log10(open.hi + 1e-12):0.0} → {10 * Math.Log10(body.hi + 1e-12):0.0}dB");
        }
    }
}
