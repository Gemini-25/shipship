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
        (double social, int exercise, double work, Sim s, int p) Day(float open, float consc, float extra, float agree, float neuro, double hours)
        {
            var s = ShipWorld.Crewed("Didim", 4, 6, 2);
            var m = s.Get<Mind>();
            int p = m.Agents.First(a => a.IsPerson).Id;
            m.SetPersonality(p, open, consc, extra, agree, neuro);
            long social = 0, work = 0, lastEx = m[p].C.ExercisedAt; int ex = 0;
            for (long t = 0; t < Time.Hours(hours); t++)
            {
                s.Step();
                if (m[p].C.Goal == GoalKind.Socialize) social++;
                if (m[p].C.Goal == GoalKind.Work) work++;
                if (m[p].C.ExercisedAt != lastEx) { ex++; lastEx = m[p].C.ExercisedAt; }
                if (Debug && consc > 0.9f && t % Time.Hours(1) == 0) Console.WriteLine($"    {Time.Clock(s.Tick)} 성실 {m.Thought(p)} · 운동 부족 {m[p].C.Unfit:0.00} · 교대 {m[p].C.ShiftHour}시");
            }
            return (social / (double)Time.TicksPerHour, ex, work / (double)Time.TicksPerHour, s, p);
        }

        // 넷은 서로 다른 세계라 함께 돌린다 (결정론 그대로 — 세계끼리 나누는 것이 없다)
        var eHi = Task.Run(() => Day(0.5f, 0.5f, 0.95f, 0.5f, 0.5f, 18)); var eLo = Task.Run(() => Day(0.5f, 0.5f, 0.05f, 0.5f, 0.5f, 18));
        var cHi = Task.Run(() => Day(0.5f, 0.95f, 0.5f, 0.5f, 0.5f, 48)); var cLo = Task.Run(() => Day(0.5f, 0.05f, 0.5f, 0.5f, 0.5f, 48)); // 이틀 — 교대에 따라 하루 한 번 빈 시간

        // ── 외향: 같은 하루 — 외향적인 사람은 사람을 찾아 어울리고 · 내향적인 사람은 덜 ──
        {
            var hi = eHi.Result; var lo = eLo.Result;
            Check("성격 · 외향: 같은 18시간, 외향적인 사람(0.95)은 내향적인 사람(0.05)보다 두 배 넘게 어울린다",
                hi.social > lo.social * 2 && hi.social > 0.5, $"어울린 시간 외향 {hi.social:0.0}시간 · 내향 {lo.social:0.0}시간");
        }

        // ── 성실: 이틀 — 성실한 사람은 운동을 챙긴다 (게으른 사람보다 많이) ──
        {
            var hi = cHi.Result; var lo = cLo.Result;
            Check("성격 · 성실: 이틀(48시간) 동안 성실한 사람(0.95)은 운동을 거르지 않고 · 게으른 사람(0.05)은 덜 한다 (운동 습관)",
                hi.exercise >= 2 && hi.exercise > lo.exercise, $"운동 성실 {hi.exercise}번 · 게으름 {lo.exercise}번 · 일한 시간 {hi.work:0.0} / {lo.work:0.0}시간");
        }

        // ── 잠이 밀려 저녁에 깨어 있는 사람(교대 0시: 16~24시가 '밤')도 졸리지 않으면 운동한다 (예전: 교대에 맞춘 밤 시간에 막혔다) ──
        {
            var s = ShipWorld.Crewed("Didim", 6, 1, 0);
            var m = s.Get<Mind>();
            int p = m.Agents.First(a => a.IsPerson).Id;
            m[p].C.ShiftHour = 0;
            s.Run(Time.Hours(17));
            bool exercised = false;
            for (long t = 0; t < Time.Hours(2); t++)
            {
                ref var c = ref m[p].C;
                c.Sleepy = 0.1f; c.Hunger = 0; c.Bored = 0; c.Lonely = 0; c.Unfit = 1;
                s.Step();
                exercised |= m[p].C.Goal == GoalKind.Exercise;
            }
            Check("잠이 밀려 저녁에 깨어 있으면(졸리지 않으면) 교대의 '밤' 시간이어도 운동한다", exercised, $"17~19시 운동 {exercised} · {m.Thought(p)}");
        }

        // ── 어울릴 사람이 없으면 (모두 자거나 일한다) 외로워도 혼자 휴게실에 앉아 몇 시간씩 기다리지 않는다 ──
        {
            var s = ShipWorld.Crewed("Didim", 5, 1, 0);
            var m = s.Get<Mind>();
            int p = m.Agents.First(a => a.IsPerson).Id;
            long alone = 0;
            for (long t = 0; t < Time.Hours(6); t++) { m[p].C.Lonely = 1; s.Step(); if (m[p].C.Goal == GoalKind.Socialize) alone++; }
            Check("어울릴 사람이 아무도 없으면 외로워도 혼자 '어울린다'로 시간을 보내지 않는다 (6시간 중 거의 0)", alone < Time.Minutes(10),
                $"혼자 어울린 시간 {alone / (double)Time.TicksPerHour:0.0}시간");
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
                ch.Ignite(At(15, 1), 3e6, s.Causes.Add(CauseKind.Story, "의자 밑에서 불씨가 튀었다", s.Tick));
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
