using ShipEngine;

namespace Lab;

/// <summary>⑧ 성격 — 다섯 축(Big Five)이 사람마다 다르고 · 같은 자리에서 다른 선택을 만든다. docs/DEPTH.md</summary>
public static partial class Program
{
    private static void PersonRules()
    {
        if (!Do("성격")) return;
        Console.WriteLine("\n[⑧ 성격 — 다섯 축]");
        var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        static double Corr(IList<float> x, IList<float> y)
        {
            double mx = x.Average(), my = y.Average(), sxy = 0, sxx = 0, syy = 0;
            for (int i = 0; i < x.Count; i++) { sxy += (x[i] - mx) * (y[i] - my); sxx += (x[i] - mx) * (x[i] - mx); syy += (y[i] - my) * (y[i] - my); }
            return sxy / Math.Sqrt(sxx * syy + 1e-12);
        }

        // ── 분포: 한누리호 100명 — 다섯 축은 평균 0.5 · 표준 편차 0.17 언저리 (정규) · 용기는 외향(+) · 신경성(−)과 함께 간다 ──
        {
            var s = ShipWorld.Crewed("Hannuri", 1, 100, 0);
            var ps = s.Get<Mind>().Agents.Where(a => a.IsPerson).Select(a => a.C).ToList();
            var axes = new (string name, List<float> v)[] { ("열림", ps.Select(c => c.Open).ToList()), ("성실", ps.Select(c => c.Consc).ToList()), ("외향", ps.Select(c => c.Extra).ToList()), ("친화", ps.Select(c => c.Agree).ToList()), ("신경성", ps.Select(c => c.Neuro).ToList()) };
            double Sd(List<float> v) { double m = v.Average(); return Math.Sqrt(v.Average(x => (x - m) * (x - m))); }
            bool shape = axes.All(ax => Math.Abs(ax.v.Average() - 0.5) < 0.06 && Sd(ax.v) is > 0.12 and < 0.22);
            double be = Corr(ps.Select(c => c.Brave).ToList(), ps.Select(c => c.Sociable).ToList()), bn = Corr(ps.Select(c => c.Brave).ToList(), ps.Select(c => 1 - c.Calm).ToList());
            Check("성격 분포: 한누리호 100명 — 다섯 축은 평균 0.5 · 표준 편차 0.12~0.22 · 용기는 사교성(외향)과 함께, 불안(신경성)과 거꾸로 간다",
                shape && be > 0.2 && bn < -0.3 && ps.Count == 100,
                string.Join(" · ", axes.Select(ax => $"{ax.name} {ax.v.Average():0.00}±{Sd(ax.v):0.00}")) + $" · 용기~외향 {be:0.00} · 용기~신경성 {bn:0.00}");
        }

        // 같은 배(디딤호 6명 · 로봇 2) · 같은 시드에서 한 사람의 성격만 바꿔 본다
        //   하루를 기다리지 않고 차려 놓는다: 그 사람은 지금 일과가 빈 시간(교대를 맞춘다) · 졸리지도 배고프지도 않고 · 몸은 운동이 고프고 · 조금 외롭다 — 그다음 몇 시간 무엇을 고르나
        (double social, int exercise, double work, Sim s, int p) Day(float open, float consc, float extra, float agree, float neuro, double hours)
        {
            var s = ShipWorld.Crewed("Didim", 4, 6, 2);
            var m = s.Get<Mind>();
            int p = m.Agents.First(a => a.IsPerson).Id;
            m.SetPersonality(p, open, consc, extra, agree, neuro);
            ref var c0 = ref m[p].C;
            c0.ShiftHour = ((int)Time.HourOfDay(s.Tick) + 24 - 10) % 24; // 지금이 교대 시작 10시간 뒤 — 일도 잠도 아닌 6시간
            c0.Sleepy = 0.1f; c0.Hunger = 0.1f; c0.Thirst = 0; c0.Bored = 0.3f; c0.Lonely = 0.6f; c0.Unfit = 0.9f;
            long social = 0, work = 0, lastEx = m[p].C.ExercisedAt; int ex = 0;
            for (long t = 0; t < Time.Hours(hours); t++)
            {
                s.Step();
                if (m[p].C.Goal == GoalKind.Socialize) social++;
                if (m[p].C.Goal == GoalKind.Work) work++;
                if (m[p].C.ExercisedAt != lastEx) { ex++; lastEx = m[p].C.ExercisedAt; }
                if (Debug && t % Time.Minutes(30) == 0) Console.WriteLine($"    성실 {consc} 외향 {extra} {Time.Clock(s.Tick)} {m.Thought(p)} · 운동 부족 {m[p].C.Unfit:0.00} · 외로움 {m[p].C.Lonely:0.00}");
            }
            return (social / (double)Time.TicksPerHour, ex, work / (double)Time.TicksPerHour, s, p);
        }

        // 넷은 서로 다른 세계라 함께 돌린다 (결정론 그대로 — 세계끼리 나누는 것이 없다)
        var eHi = Task.Run(() => Day(0.5f, 0.5f, 0.95f, 0.5f, 0.5f, 4)); var eLo = Task.Run(() => Day(0.5f, 0.5f, 0.05f, 0.5f, 0.5f, 4));
        var cHi = Task.Run(() => Day(0.5f, 0.95f, 0.5f, 0.5f, 0.5f, 3)); var cLo = Task.Run(() => Day(0.5f, 0.05f, 0.5f, 0.5f, 0.5f, 3));

        // ── 외향: 같은 빈 시간 — 외향적인 사람은 사람을 찾아 어울리고 · 내향적인 사람은 덜 ──
        {
            var hi = eHi.Result; var lo = eLo.Result;
            Check("성격 · 외향: 같은 빈 4시간, 외향적인 사람(0.95)은 내향적인 사람(0.05)보다 두 배 넘게 어울린다",
                hi.social > lo.social * 2 && hi.social > 0.5, $"어울린 시간 외향 {hi.social:0.0}시간 · 내향 {lo.social:0.0}시간");
        }

        // ── 성실: 몸이 똑같이 운동을 바랄 때 — 성실한 사람은 운동을 챙긴다 (게으른 사람보다 많이) ──
        {
            var hi = cHi.Result; var lo = cLo.Result;
            Check("성격 · 성실: 똑같이 운동이 고픈 빈 3시간, 성실한 사람(0.95)은 운동하고 · 게으른 사람(0.05)은 덜 한다 (운동 습관)",
                hi.exercise >= 1 && hi.exercise > lo.exercise, $"운동 성실 {hi.exercise}번 · 게으름 {lo.exercise}번 · 일한 시간 {hi.work:0.0} / {lo.work:0.0}시간");
        }

        // ── 잠이 밀려 교대의 '밤' 시간에 깨어 있는 사람(교대 8시: 0~8시가 '밤')도 졸리지 않으면 운동한다 (예전: 교대에 맞춘 밤 시간에 막혔다) ──
        {
            var s = ShipWorld.Crewed("Didim", 6, 1, 0);
            var m = s.Get<Mind>();
            int p = m.Agents.First(a => a.IsPerson).Id;
            m[p].C.ShiftHour = 8;
            s.Run(5);
            bool exercised = false, night = false;
            for (long t = 0; t < Time.Hours(2); t++)
            {
                ref var c = ref m[p].C;
                c.Sleepy = 0.1f; c.Hunger = 0; c.Bored = 0; c.Lonely = 0; c.Unfit = 1;
                s.Step();
                exercised |= m[p].C.Goal == GoalKind.Exercise;
                night |= Time.HourOfDay(s.Tick) < 8;
            }
            Check("잠이 밀려 교대의 '밤' 시간에 깨어 있으면(졸리지 않으면) 운동한다", exercised && night, $"교대 8시 · {Time.Clock(s.Tick)} · 운동 {exercised} · {m.Thought(p)}");
        }

        // ── 어울릴 사람이 없으면 (모두 자거나 일한다) 외로워도 혼자 휴게실에 앉아 몇 시간씩 기다리지 않는다 ──
        {
            var s = ShipWorld.Crewed("Didim", 5, 1, 0);
            var m = s.Get<Mind>();
            int p = m.Agents.First(a => a.IsPerson).Id;
            long alone = 0;
            for (long t = 0; t < Time.Hours(2); t++) { m[p].C.Lonely = 1; s.Step(); if (m[p].C.Goal == GoalKind.Socialize) alone++; }
            Check("어울릴 사람이 아무도 없으면 외로워도 혼자 '어울린다'로 시간을 보내지 않는다 (두 시간 중 거의 0 — 예전엔 내내)", alone < Time.Minutes(10),
                $"혼자 어울린 시간 {alone / (double)Time.TicksPerHour:0.0}시간");
        }

        // ── 어울리러 가면 쉬는 사람 곁(말이 닿는 거리) 자리에 앉는다 — 꾸민 배는 의자가 많아 예전엔 가까운 빈 의자에 혼자 앉았다 (대화 15 → 9) ──
        {
            var s = ShipWorld.Crewed("Didim", 7, 2, 0);
            var m = s.Get<Mind>();
            var ps = m.Agents.Where(a => a.IsPerson).Select(a => a.Id).ToArray();
            int p = ps[0], q = ps[1];
            int off = ((int)Time.HourOfDay(s.Tick) + 24 - 10) % 24;   // 둘 다 일도 잠도 아닌 빈 시간
            void Hold(int x, float bored, float lonely) { ref var c = ref m[x].C; c.ShiftHour = off; c.Sleepy = 0; c.Hunger = 0; c.Thirst = 0; c.Unfit = 0; c.Bored = bored; c.Lonely = lonely; }
            bool Seated(int x) => m[x].C.StepIndex < m.Agents[x].Plan.Count && m.Agents[x].Plan[m[x].C.StepIndex].Kind == StepKind.Use;
            // q가 먼저 쉬러 가 자리를 잡는다 (p는 아직 외롭지 않다)
            for (long t = 0; t < Time.Hours(1) && !(m[q].C.Goal == GoalKind.Relax && Seated(q)); t++) { Hold(q, 1, 0); Hold(p, 0, 0); s.Step(); }
            bool qSeated = m[q].C.Goal == GoalKind.Relax && Seated(q);
            int dist = -1;
            for (long t = 0; t < Time.Hours(1) && dist < 0; t++)
            {
                Hold(q, 1, 0); Hold(p, 0, 1); s.Step();
                if (m[p].C.Goal == GoalKind.Socialize && Seated(p)) dist = m.Dist(m[p].C.Cell, m[q].C.Cell);
            }
            Check("어울리러 가면 쉬는 사람 곁(말이 닿는 거리) 자리에 앉는다 (의자 많은 방에서 혼자 먼 의자에 앉지 않는다)",
                qSeated && dist >= 0 && dist < Society.TalkReach,
                $"먼저 쉬는 이 자리 잡음 {qSeated} · 어울리러 온 이와의 거리 {dist}칸 (말이 닿는 거리 {Society.TalkReach - 1}칸 안) · {m.Thought(p)}");
        }

        // ── 신경성: 불을 본 순간 — 불안한 사람은 더 겁먹고 · 차분한 사람은 덜 ──
        {
            float PeakFear(float neuro, float extra)
            {
                var s = Worlds.Full(lay, 1);
                var m = s.Get<Mind>(); var ch = s.Get<Chemistry>();
                int p = m.AddPerson(s, At(12, 2));
                m.SetPersonality(p, 0.5f, 0.5f, extra, 0.5f, neuro);
                s.Run(3);
                ch.Ignite(At(15, 1), 3e6, s.Causes.Add(CauseKind.Setup, "의자 밑에서 불씨가 튀었다", s.Tick));
                float peak = 0;
                for (int k = 0; k < Time.Minutes(3); k++) { s.Step(); peak = Math.Max(peak, m[p].C.Fear); }
                if (Debug) Console.WriteLine($"    신경성 {neuro} → 두려움 {peak:0.00} · {m.Thought(p)}");
                return peak;
            }
            float anxious = PeakFear(0.95f, 0.2f), steady = PeakFear(0.05f, 0.8f);
            Check("성격 · 신경성: 같은 불을 보고 불안한 사람(0.95)은 차분한 사람(0.05)보다 훨씬 겁먹는다",
                anxious > steady + 0.2f, $"가장 큰 두려움 불안 {anxious:0.00} · 차분 {steady:0.00}");
        }
    }
}
