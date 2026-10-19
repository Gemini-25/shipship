using ShipEngine;

namespace Lab;

/// <summary>
/// 판단 이관 (docs/ENGINE.md §3 9 — 함장 결정): 사람 · 로봇 · 주컴퓨터가 하는 모든 행동은 각자의 판단 엔진이 정하고 ·
/// 사회 · 생명 · 물리 같은 월드 엔진은 그 행동이 세상을 어떻게 바꾸는지 계산할 뿐이다.
/// </summary>
public static partial class Program
{
    private static void JudgmentRules()
    {
        if (!Do("판단 이관")) return;
        Console.WriteLine("\n[판단 이관 — 행동은 판단 엔진 · 결과는 월드 엔진]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var legend = new Dictionary<char, string> { ['m'] = "MealDispenser", ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer", ['e'] = "Bed" };
        var lay = Worlds.Ascii(CouncilShip, legend);
        int At(int x, int y) => Worlds.At(lay, x, y);

        (Sim s, Mind m, Society soc, List<int> p) World(int n, ulong seed)
        {
            var s = Worlds.Full(lay, seed);
            var m = s.Get<Mind>(); var soc = s.Get<Society>();
            var p = new List<int>();
            for (int i = 0; i < n; i++) { int a = m.AddPerson(s, At(2 + (i % 3) * 2, 1 + (i / 3) * 2)); m.SetPersonality(a, 0.5f, 0.6f, 0.6f, 0.5f, 0.4f); p.Add(a); }
            s.Run(2);
            return (s, m, soc, p);
        }
        // 틱마다 그 틱의 행동을 모은다 (마음은 다음 틱 처음에 지난 행동을 비운다)
        var seen = new Dictionary<Mind, long>();   // 마음마다 어디까지 모았나 (행동은 세 틱 남는다)
        void Step(Sim s, Mind m, List<SocialAct> acts, List<int> keep, int asleep = -1)
        {
            s.Step();
            acts.AddRange(m.ActsSince(seen.GetValueOrDefault(m, m.ActBase)));
            seen[m] = m.ActsTotal;
            var life = s.Get<Life>();
            foreach (int a in keep) { ref var c = ref m[a].C; c.Thirst = 0; c.Hunger = 0; c.Sleepy = a == asleep ? 1 : 0; life[c.Body].Asleep = (byte)(a == asleep ? 1 : 0); }
        }
        void Grudges(Society soc, List<int> p)
        {
            int N = soc.N, x = p[0];
            soc.Grudge[p[1] * N + x] = 0.6f; soc.Grudge[p[2] * N + x] = 0.6f; soc.Grudge[p[3] * N + x] = 0.4f;
        }

        // ── (가) 모임: 부르기 · 말 · 미루기는 사람의 행동 — 회의가 열린 사실마다 같은 틱에 부른 이의 '부르기'가 · 모임의 말마다 그 사람의 '말하기'가 있다 ──
        {
            var (s, m, soc, p) = World(5, 3);
            Grudges(soc, p);
            var acts = new List<SocialAct>();
            long f0 = s.Facts.Total;
            m.CallCouncilNow(s);
            acts.AddRange(m.Acts); seen[m] = m.ActsTotal;
            for (int k = 0; k < Time.Minutes(30); k++) Step(s, m, acts, p);
            var opened = s.Facts.Since(f0).Where(f => f.Kind == FactKind.Meeting && f.Size > 0).ToList();
            var meetCause = opened.Count > 0 ? opened[0].Cause : -1;
            var said = soc.Sayings.Where(y => y.Tick >= s.Tick - Time.Minutes(30) && s.Causes[y.Line].Parent == meetCause).ToList();   // 모임의 말 (이야기는 (나))
            int callsMatched = opened.Count(f => acts.Any(a => a.Kind == SocialActKind.CallMeeting && a.Tick == f.Tick && a.Actor == f.Actor && a.Cause == f.Cause));
            int saysMatched = said.Count(y => acts.Any(a => a.Kind == SocialActKind.Speak && a.Tick == y.Tick && a.Actor == y.Speaker && a.Cause == y.Line));
            bool verdict = s.Facts.Since(f0).Any(f => f.Kind == FactKind.Verdict);
            if (Debug) foreach (var a in acts) Console.WriteLine($"    {Time.Clock(a.Tick)} {a.Kind} {m.NameOf(a.Actor)} {s.Causes.Text(a.Cause)}");
            Check("(가) 모임은 사람이 연다: 회의가 열린 사실마다 같은 틱에 부른 이의 '부르기' 행동이 · 모임의 말마다 그 사람의 '말하기' 행동(같은 원인 마디)이 있다 — 사회 엔진은 부르지도 말하지도 않는다",
                opened.Count == 1 && callsMatched == 1 && said.Count >= 4 && saysMatched == said.Count && verdict,
                $"열림 {opened.Count} (부르기 맞음 {callsMatched}) · 말 {said.Count} (말하기 맞음 {saysMatched}) · 판결 {verdict}");
        }

        // ── (가) 잠든 이는 말하지 않는다: 말할 차례가 오기 전에 잠든 이는 건너뛰고 · 그의 표는 세지 않는다 (옛 사회 엔진은 잠든 이의 말도 지어냈다) ──
        {
            var (s, m, soc, p) = World(5, 3);
            Grudges(soc, p);
            var acts = new List<SocialAct>();
            long f0 = s.Facts.Total;
            m.CallCouncilNow(s);
            int sleeper = -1;
            for (int k = 0; k < Time.Minutes(30); k++)
            {
                if (sleeper < 0 && soc.Meeting.Phase == 1 && soc.SpeakerNow >= 0)
                {
                    sleeper = p.Where(a => a != p[0] && a != soc.SpeakerNow && a != soc.Meeting.Caller).Last();   // 아직 말하지 않은 이 하나가 잠든다
                }
                Step(s, m, acts, p, sleeper);
            }
            var verdict = s.Facts.Since(f0).FirstOrDefault(f => f.Kind == FactKind.Verdict);
            string text = verdict.Kind == FactKind.Verdict ? s.Causes.Text(verdict.Cause) : "-";
            bool sleeperSpoke = soc.Sayings.Any(y => y.Tick >= s.Tick - Time.Minutes(30) && y.Speaker == sleeper);
            Check("(가) 잠든 이는 말하지 않는다: 말할 차례 전에 잠든 이는 건너뛰고 그의 표는 세지 않는다 — 판결은 깬 이들로 (넷 가운데 셋)",
                sleeper >= 0 && !sleeperSpoke && verdict.Kind == FactKind.Verdict && text.Contains("3명 중"),
                $"잠든 이 {(sleeper >= 0 ? m.NameOf(sleeper) : "-")} · 그의 말 {sleeperSpoke} · '{text}'");
        }

        // ── (나) 대화: 말 걸기 · 이야기 · 대답은 사람의 행동 — 대화 사실마다 같은 틱에 그 둘의 '대화' 행동이 있고 · 밤의 규칙으로 삼킨 말도 그 사람의 행동이다 ──
        {
            var (s, m, soc, p) = World(4, 7);
            int N = soc.N;
            foreach (int a in p) foreach (int b in p) if (a != b) soc.Known[a * N + b] = 0.6f;
            soc.Grudge[p[1] * N + p[0]] = 0.3f;   // 따질 거리 하나 (덤빌 만큼은 아니다)
            foreach (int a in p) m.SetPersonality(a, 0.5f, 0.8f, 0.6f, 0.5f, 0.4f);
            var acts = new List<SocialAct>();
            long f0 = s.Facts.Total;
            for (int k = 0; k < Time.Hours(2); k++) { Step(s, m, acts, p); foreach (int a in p) m[a].C.Bored = 0; }
            var talks = s.Facts.Since(f0).Where(f => f.Kind == FactKind.Talk).ToList();
            var tActs = acts.Where(a => a.Kind == SocialActKind.Talk).ToList();
            int matched = talks.Count(f => tActs.Any(a => a.Tick == f.Tick && (a.Actor == f.Actor && a.Target == f.Other || a.Actor == f.Other && a.Target == f.Actor)));
            // 밤의 규칙: 한 사람이 식당에서 잠들었고 · 성실한 셋 — 말을 삼킨 수 = '말을 삼켰다' 행동의 수
            var (s2, m2, soc2, p2) = World(4, 7);
            int N2 = soc2.N;
            foreach (int a in p2) foreach (int b in p2) if (a != b) soc2.Known[a * N2 + b] = 0.6f;
            foreach (int a in p2) m2.SetPersonality(a, 0.5f, 0.8f, 0.6f, 0.95f, 0.4f);
            soc2.SetRuleNow(1);
            var acts2 = new List<SocialAct>();
            long q0 = soc2.QuietKept;
            for (int k = 0; k < Time.Hours(2); k++) { Step(s2, m2, acts2, p2, p2[3]); foreach (int a in p2) m2[a].C.Bored = 0; }
            int keeps = acts2.Count(a => a.Kind == SocialActKind.KeepQuiet);
            if (Debug) foreach (var f in talks) Console.WriteLine($"    {Time.Clock(f.Tick)} {m.NameOf(f.Actor)}→{m.NameOf(f.Other)} {s.Causes.Text(f.Cause)}");
            Check("(나) 대화는 사람이 한다: 대화 사실마다 같은 틱에 그 둘의 '대화' 행동(말을 꺼낸 이 · 이야기 · 듣는 이의 대답)이 있다 / 밤의 규칙으로 삼킨 말은 그 사람의 '말을 삼켰다' 행동이다",
                talks.Count >= 3 && matched == talks.Count && tActs.Count == talks.Count && keeps > 0 && soc2.QuietKept - q0 == keeps,
                $"대화 사실 {talks.Count} · 행동 {tActs.Count} · 맞음 {matched} / 삼킨 말 {soc2.QuietKept - q0} · 행동 {keeps}");
        }

        // ── (다) 다툼: 덤비기 · 주먹 · 말리기 · 편들기는 사람(로봇)의 행동 — 몸싸움 사실마다 같은 틱에 '덤빔' · 주먹 한 대마다 '친다' · 말린 로봇의 '말림' · 편든 수 = 행동 수 ──
        {
            var mlay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
            int MAt(int x, int y) => Worlds.At(mlay, x, y);
            (long fights, int attacks, int blows, int strikes, bool robotStopped, long sides, int sideActs) Feud(bool robot)
            {
                var s = Worlds.Full(mlay, 5);
                var m = s.Get<Mind>(); var so = s.Get<Society>();
                int x = m.AddPerson(s, MAt(12, 8)), y = m.AddPerson(s, MAt(15, 8)), z = m.AddPerson(s, MAt(14, 8));
                int r = robot ? m.AddRobot(s, MAt(17, 8), AgentKind.Robot, MAt(17, 8)) : -1;
                m.SetPersonality(x, 0.5f, 0.3f, 0.6f, 0.05f, 0.95f);
                m.SetPersonality(y, 0.5f, 0.5f, 0.6f, 0.05f, 0.8f);
                m.SetPersonality(z, 0.5f, 0.5f, 0.6f, 0.3f, 0.5f);
                s.Run(2);
                so.Grudge[x * so.N + y] = 0.95f; so.Grudge[y * so.N + x] = 0.6f; so.Bond[z * so.N + y] = 0.8f;
                m[x].C.Stress = 0.9f; m[x].C.Anger = 0.9f;
                var acts = new List<SocialAct>();
                long f0 = s.Facts.Total;
                var blowsOf = new Dictionary<long, int>();
                for (int k = 0; k < Time.Hours(1); k++)
                {
                    Step(s, m, acts, new List<int> { x, y, z });
                    foreach (var br in so.Brawls) blowsOf[br.Start * 64 + br.A] = br.Blows;
                }
                var fights = s.Facts.Since(f0).Where(f => f.Kind == FactKind.Fight).ToList();
                int attacks = fights.Count(f => acts.Any(a => a.Kind == SocialActKind.Attack && a.Tick == f.Tick && a.Actor == f.Actor && a.Target == f.Other && a.Cause == f.Cause));
                bool stopped = robot && acts.Any(a => a.Kind == SocialActKind.StopFight && a.Actor == r);
                return (fights.Count, attacks, blowsOf.Values.Sum(), acts.Count(a => a.Kind == SocialActKind.Strike), stopped, so.Sides, acts.Count(a => a.Kind == SocialActKind.TakeSide));
            }
            var alone = Feud(false); var watched = Feud(true);
            Check("(다) 다툼은 사람이 한다: 몸싸움 사실마다 같은 틱에 때린 이의 '덤빔' 행동 · 주먹 한 대마다 '친다' 행동 · 곁의 로봇은 '말림' 행동으로 멈춘다 · 편든 수 = '편듦' 행동 수",
                alone.fights > 0 && alone.attacks == alone.fights && alone.blows > 0 && alone.blows == alone.strikes && alone.sides == alone.sideActs
                && watched.fights > 0 && watched.attacks == watched.fights && watched.robotStopped && watched.blows == watched.strikes,
                $"홀로: 싸움 {alone.fights} (덤빔 맞음 {alone.attacks}) · 주먹 {alone.blows} / 친다 {alone.strikes} · 편듦 {alone.sides}/{alone.sideActs} / 로봇 곁: 싸움 {watched.fights} (덤빔 {watched.attacks}) · 말림 {watched.robotStopped} · 주먹 {watched.blows}/{watched.strikes}");
        }
    }
}
