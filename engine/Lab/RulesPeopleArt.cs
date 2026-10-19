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
        }
    }
}
