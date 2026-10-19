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
        void Step(Sim s, Mind m, List<SocialAct> acts, List<int> keep, int asleep = -1)
        {
            s.Step();
            acts.AddRange(m.Acts);
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
            acts.AddRange(m.Acts);
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
    }
}
