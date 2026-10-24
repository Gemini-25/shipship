using ShipEngine;
using ShipSim.Art;
using ShipView;

namespace Lab;

/// <summary>사람 그림 규칙 시험 (그래픽 결정 6 · 9 · 10) — 다친 자국 · 표정 · 기어가기 · 생활 동작은 세계(⑦ 몸 · ⑧ 마음 · ③ 낮은 머리 · 하는 일)의 값에서만 나온다.</summary>
public static partial class Program
{
    private static void PeopleArtRules()
    {
        if (!Do("사람 그림")) return;
        Console.WriteLine("\n[사람 그림]");

        // ── 몸 → 자국 · 마음 → 표정 · 하는 일 → 동작 (그림이 고르는 표) ──
        {
            var b = new Body { CoreK = 310 - 0.85 };
            bool calm = Painter.HurtOf(b) == DotHurt.None;
            b.Bleed = 0.3f; b.Burn = 0.4f; b.Dressed = 3; b.Frost = 0.2f; b.RadGy = 3; b.CoreK = 311.5;
            var h = Painter.HurtOf(b);
            bool marks = (h & (DotHurt.Bleed | DotHurt.Burn | DotHurt.TorsoWrap | DotHurt.Frost | DotHurt.RadHair | DotHurt.Sick)) == (DotHurt.Bleed | DotHurt.Burn | DotHurt.TorsoWrap | DotHurt.Frost | DotHurt.RadHair | DotHurt.Sick);
            var c = new AgentCore();
            var b0 = new Body();
            var f0 = Painter.FaceOf(c, b0);
            c.Joy = 0.8f; var f1 = Painter.FaceOf(c, b0);
            c.Fear = 0.8f; var f2 = Painter.FaceOf(c, b0);
            var bp = new Body { Pain = 0.7f }; var f3 = Painter.FaceOf(c, bp);
            c = new AgentCore { Goal = GoalKind.Eat }; var m1 = Painter.MotionOf(c, StepKind.Use, DotPose.Sit);
            var m2 = Painter.MotionOf(new AgentCore { Goal = GoalKind.Repair }, StepKind.Fix, DotPose.Work);
            var m3 = Painter.MotionOf(new AgentCore { Goal = GoalKind.Socialize }, StepKind.Use, DotPose.Sit);
            var m4 = Painter.MotionOf(new AgentCore { Goal = GoalKind.Relax, Sleepy = 0.9f }, StepKind.Use, DotPose.Sit);
            var m5 = Painter.MotionOf(new AgentCore { Goal = GoalKind.Work }, StepKind.GoTo, DotPose.Walk);
            Check("사람 그림의 표: 성한 몸은 자국 없음 · 피 · 화상 · 덮은 상처 · 동상 · 방사선 · 열이 보인다 / 덤덤 · 기쁨 → 웃음 · 겁 → 겁 · 통증이 겁보다 앞선다 / 먹기 → 손을 입으로 · 고치기 → 공구 · 말 → 손짓 · 졸림 → 졸기 · 걷기 → 없음",
                calm && marks && f0 == DotFace.Calm && f1 == DotFace.Smile && f2 == DotFace.Fear && f3 == DotFace.Pain
                && m1 == DotMotion.Drink && m2 == DotMotion.Tool && m3 == DotMotion.Talk && m4 == DotMotion.Doze && m5 == DotMotion.None,
                $"자국 {h} · 표정 {f0}/{f1}/{f2}/{f3} · 동작 {m1}/{m2}/{m3}/{m4}/{m5}");
        }

        // ── 실제 화면: 겁 · 낮은 머리를 바꾸면 그 사람의 그림이 바뀌고 · 그리기는 세계를 바꾸지 않는다 ──
        {
            var s = ShipWorld.Crewed("Didim", 3, 2, 0);
            s.Run(30);
            var L = s.Layout; var m = s.Get<Mind>(); var life = s.Get<Life>();
            var a = m.Agents.First(x => x.IsPerson);
            int cell = a.C.Cell;
            int x0 = Math.Max(0, L.X(cell) - 3), y0 = Math.Max(0, L.Y(cell) - 3);
            var p = new Painter(L);
            int Diff(Img i1, Img i2) { int n = 0; for (int i = 0; i < i1.Px.Length; i += 4) if (i1.Px[i] != i2.Px[i] || i1.Px[i + 1] != i2.Px[i + 1] || i1.Px[i + 2] != i2.Px[i + 2]) n++; return n; }
            Img Shot() { var im = p.Frame(s, x0, y0, 7, 7, 0); var copy = new Img(im.W, im.H); Array.Copy(im.Px, copy.Px, im.Px.Length); return copy; }
            a.Path.Clear(); a.PathIdx = 0;
            var before = s.Save();
            var plain = Shot();
            bool same = before.AsSpan().SequenceEqual(s.Save());
            float fear = a.C.Fear; a.C.Fear = 0.9f; var scared = Shot(); a.C.Fear = fear;
            ref var b = ref life[a.C.Body];
            byte low = b.Low, asleep = b.Asleep; b.Low = 1; b.Asleep = 0; var crawl = Shot(); b.Low = low; b.Asleep = asleep;
            int dFear = Diff(plain, scared), dCrawl = Diff(plain, crawl);
            Check("화면의 사람: 두려움을 올리면 얼굴이 바뀌고 (몇 도트) · 머리를 낮추면 엎드린 그림이 된다 (많이) · 그리기는 세계를 바꾸지 않는다",
                same && dFear is > 0 and < 40 && dCrawl > 100, $"같은 세계 {same} · 겁 {dFear}도트 · 기기 {dCrawl}도트");

            // ── 얼룩진 옷 · 지린 옷 (5단 A3 — ⑦ Body.Dirt · Body.Wet): 더러우면 옷에 갈색 점 · 지리면 다리 윗부분이 어둡다 · 우주복은 덮는다 · 씻으면 처음 그림 ──
            p.Lighting = false;   // 빛깔만 견준다 (사람 도트는 색표 그대로)
            byte suit = b.Suit, wet = b.Wet; float dirt = b.Dirt; asleep = b.Asleep; byte conscious = b.Conscious;
            int look = a.C.LookAt; long until = a.C.LookUntil;
            b.Asleep = 0; b.Conscious = 1; b.Dirt = 0; b.Wet = 0; a.C.LookUntil = -1;   // 서서 앞을 보는 그림 (누운 그림은 돌린 같은 칠 — 자리를 세기 쉽게)
            int sx = (L.X(cell) - x0) * Painter.T + Painter.T / 2 - DotPeople.Cx, sy = (L.Y(cell) - y0) * Painter.T + Painter.T - DotPeople.Foot - 2;   // 사람 그림 (24×40)의 왼쪽 위
            var stainCols = new[] { Rgba.Hex("#2a1d12"), Rgba.Hex("#4a3420"), Rgba.Hex("#6b4e30") };
            int Brown(Img im) { int n = 0; for (int yy = 0; yy < DotPeople.FH; yy++) for (int xx = 0; xx < DotPeople.FW; xx++) { var q = im.Get(sx + xx, sy + yy); if (Array.IndexOf(stainCols, q) >= 0) n++; } return n; }
            int Darker(Img i1, Img i2, int r0, int r1) { int n = 0; for (int yy = r0; yy <= r1; yy++) for (int xx = 0; xx < DotPeople.FW; xx++) { var q1 = i1.Get(sx + xx, sy + yy); var q2 = i2.Get(sx + xx, sy + yy); if (q2.R + q2.G + q2.B < q1.R + q1.G + q1.B) n++; } return n; }
            int Rows(Img i1, Img i2, int r0, int r1) { int n = 0; for (int yy = r0; yy <= r1; yy++) for (int xx = 0; xx < DotPeople.FW; xx++) if (i1.Get(sx + xx, sy + yy) != i2.Get(sx + xx, sy + yy)) n++; return n; }
            var face = new Img(Painter.FaceW, Painter.FaceH);
            var clean = Shot(); int n0 = Brown(clean); long k0 = p.Portrait(s, a.Id, face, 0, 0);
            b.Dirt = 0.8f; var dirty = Shot(); int n1 = Brown(dirty); long k1 = p.Portrait(s, a.Id, face, 0, 0);
            b.Dirt = 0; b.Wet = 1; var soiled = Shot(); int legDark = Darker(clean, soiled, 26, 35), upper = Rows(clean, soiled, 0, 25);   // 다리 (몸통 아래) 윗부분 · 그 위는 그대로
            b.Suit = 1; b.Dirt = 0; b.Wet = 0; var suit0 = Shot();
            b.Dirt = 0.8f; b.Wet = 1; var suit1 = Shot(); int nSuit = Brown(suit1), dSuit = Diff(suit0, suit1);
            b.Suit = 0; life.Rinse(a.C.Body, Life.ShowerMol); var rinsed = Shot(); int nRinse = Brown(rinsed), dRinse = Diff(clean, rinsed); long k2 = p.Portrait(s, a.Id, face, 0, 0);
            Check("얼룩진 옷 · 지린 옷: 더러움 0.8 → 옷에 갈색 점 10 넘게 더 · 얼굴 줄 열쇠도 바뀐다 · 지림 → 다리 윗부분 어두운 점 6 넘게 (몸통 위는 그대로) · 우주복은 덮는다 · 샤워 한 번 씻으면 처음 그림",
                n1 > n0 + 10 && k1 != k0 && legDark > 6 && upper == 0 && nSuit == n0 && dSuit == 0 && nRinse == n0 && dRinse == 0 && k2 == k0,
                $"갈색 {n0} → {n1} · 열쇠 {(k1 != k0 ? "바뀜" : "같음")} · 다리 어두운 점 {legDark} · 위 바뀐 점 {upper} · 우주복 갈색 {nSuit} · 바뀐 점 {dSuit} · 씻은 뒤 갈색 {nRinse} · 바뀐 점 {dRinse} · 열쇠 {(k2 == k0 ? "처음" : "다름")}");

            // 옆 그림 (동쪽을 본다): 신발 줄 (36 · 37줄)은 지려도 · 더러워도 그대로 (옆 그림의 부위 3은 신발이다) · 다리는 어두워진다
            a.C.LookAt = cell + 1; a.C.LookUntil = s.Tick + 1000;
            b.Dirt = 0; b.Wet = 0; var side0 = Shot();
            b.Dirt = 0.9f; b.Wet = 1; var side1 = Shot();
            int shoes = Rows(side0, side1, DotPeople.Foot - 1, DotPeople.Foot), sideLeg = Darker(side0, side1, 26, 35);
            Check("얼룩진 옷 · 지린 옷 옆 그림: 신발 줄은 그대로 · 다리 윗부분은 어두워진다",
                shoes == 0 && sideLeg > 6 && Diff(side0, clean) > 0, $"신발 줄 바뀐 점 {shoes} · 다리 어두운 점 {sideLeg} · 옆 그림 {Diff(side0, clean)}도트 다름");
            a.C.LookAt = look; a.C.LookUntil = until;

            // 누운 · 기는 · 업힌 그림 (A3 검토 — 그 갈래를 지워도 초록이었다): 같은 칠 — 더러움 0.8 → 갈색 점 10 넘게 더 · 지림 → 어두운 점 (돌린 그림이라 그림판 전체에서 센다)
            int BrownAll(Img im) { int n = 0; for (int i = 0; i < im.Px.Length; i += 4) if (Array.IndexOf(stainCols, new Rgba(im.Px[i], im.Px[i + 1], im.Px[i + 2])) >= 0) n++; return n; }
            int DarkerAll(Img i1, Img i2) { int n = 0; for (int i = 0; i < i1.Px.Length; i += 4) if (i2.Px[i] + i2.Px[i + 1] + i2.Px[i + 2] < i1.Px[i] + i1.Px[i + 1] + i1.Px[i + 2]) n++; return n; }
            (int brown, int dark) Stains(int body)   // 그 몸을 깨끗이 · 더러움 0.8 · 지림으로 찍어 견준다
            {
                life[body].Dirt = 0; life[body].Wet = 0; var q0 = Shot();
                life[body].Dirt = 0.8f; var q1 = Shot();
                life[body].Dirt = 0; life[body].Wet = 1; var q2 = Shot();
                life[body].Wet = 0;
                return (BrownAll(q1) - BrownAll(q0), DarkerAll(q0, q2));
            }
            byte low0 = b.Low;
            b.Asleep = 1; var lie = Stains(a.C.Body); b.Asleep = 0;   // 누운 그림 (자는 이)
            b.Low = 1; var crawled = Stains(a.C.Body); b.Low = low0;   // 기는 그림 (머리를 낮췄다)
            var o = m.Agents.First(x => x.IsPerson && x != a);   // 업힌 사람 (업은 이의 어깨 위 누운 그림)
            int carrying = a.C.Carrying, carriedBy = o.C.CarriedBy;
            a.C.Carrying = o.C.Body; o.C.CarriedBy = a.Id;
            var carried = Stains(o.C.Body);
            a.C.Carrying = carrying; o.C.CarriedBy = carriedBy;
            Check("얼룩진 옷 · 지린 옷 누운 · 기는 · 업힌 그림: 더러움 0.8 → 갈색 점 10 넘게 더 · 지림 → 어두운 점 3 넘게 (서기 그림과 같은 칠)",
                lie.brown > 10 && lie.dark > 3 && crawled.brown > 10 && crawled.dark > 3 && carried.brown > 10 && carried.dark > 3,
                $"누운 갈색 +{lie.brown} · 어두운 점 {lie.dark} · 기는 +{crawled.brown} · {crawled.dark} · 업힌 +{carried.brown} · {carried.dark}");
            b.Suit = suit; b.Wet = wet; b.Dirt = dirt; b.Asleep = asleep; b.Conscious = conscious;
        }
    }
}
