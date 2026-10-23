using ShipEngine;

namespace Lab;

/// <summary>⑨ 회의 · 재판 · 선거 · 반란 (깊이 바퀴 3 — docs/ENGINE.md ⑨): 쌓인 원한 · 굶주림 · 무너진 대표가 안건이 되고 · 모이고 · 말하고 · 표결하고 · 그 결과가 벌 · 앙금 · 지도자 · 배급 거부로 이어진다.</summary>
public static partial class Program
{
    private const string CouncilShip = @"
##############################
#..e.e.e.#.........#.........#
#........D....m....D..c.pb...#
#..e.e.e.#.........#.........#
##############################";

    private static void CouncilRules()
    {
        if (!Do("회의")) return;
        Console.WriteLine("\n[⑨ 회의 · 재판 · 선거 · 반란]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var legend = new Dictionary<char, string> { ['m'] = "MealDispenser", ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer", ['e'] = "Bed" };
        var lay = Worlds.Ascii(CouncilShip, legend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        int mess = lay.Room[At(14, 2)];

        // 세계: n명 (깨어 있고 · 모임에 갈 만한 성격) — 시험마다 관계를 차린다
        (Sim s, Mind m, Society soc, List<int> p) World(int n, ulong seed)
        {
            var s = Worlds.Full(lay, seed);
            var m = s.Get<Mind>(); var soc = s.Get<Society>();
            var p = new List<int>();
            for (int i = 0; i < n; i++) { int a = m.AddPerson(s, At(2 + (i % 3) * 2, 1 + (i / 3) * 2)); m.SetPersonality(a, 0.5f, 0.6f, 0.6f, 0.5f, 0.4f); p.Add(a); }
            s.Run(2);
            return (s, m, soc, p);
        }
        void Keep(Sim s, Mind m, List<int> p, bool hunger = true)
        {
            foreach (int a in p) { ref var c = ref m[a].C; c.Sleepy = 0; c.Thirst = 0; if (hunger) c.Hunger = 0; s.Get<Life>()[c.Body].Asleep = 0; }
        }
        void Run(Sim s, Mind m, List<int> p, double minutes, bool hunger = true) { for (int k = 0; k < Time.Minutes(minutes); k++) { s.Step(); Keep(s, m, p, hunger); } }
        string Story(Sim s, FactKind k) { var f = s.Facts.Since(0).LastOrDefault(x => x.Kind == k); return f.Kind == k ? s.Causes.Story(f.Cause) : "-"; }

        // ── 재판: 셋이 한 사람을 원망하면 (합 1.6) 회의가 열리고 · 식당에 모여 한 마디씩 · 과반이 잘못이라 하면 사흘 벌 당번 —
        //    원망은 반으로 풀리고(정의가 섰다) · 피고는 고발한 이를 원망하고 · 그의 편을 든 친구는 고발한 이에게 앙금 조금 (와서 말했으니 조금) ──
        {
            var (s, m, soc, p) = World(5, 3);
            int x = p[0], acc = p[1], b = p[2], c2 = p[3], friend = p[4];
            int N = soc.N;
            soc.Grudge[acc * N + x] = 0.6f; soc.Grudge[b * N + x] = 0.6f; soc.Grudge[c2 * N + x] = 0.4f;
            soc.Bond[friend * N + x] = 0.8f; soc.Bond[x * N + friend] = 0.8f;
            long f0 = s.Facts.Total;
            m.CallCouncilNow(s);
            Run(s, m, p, 14);
            int gathered = p.Count(a => m.RoomOf(m[a].C.Cell) == mess);
            Run(s, m, p, 12);
            var verdict = s.Facts.Since(f0).FirstOrDefault(f => f.Kind == FactKind.Verdict);
            string story = Story(s, FactKind.Verdict);
            var lines = soc.Sayings.Where(y => y.Tick >= s.Tick - Time.Minutes(15)).Select(y => s.Causes.Text(y.Line)).ToList();
            if (Debug) { Console.WriteLine($"    {story}"); foreach (var l in lines) Console.WriteLine($"      {l}"); }
            Check("재판: 셋이 한 사람을 원망하면 회의가 열려 식당에 모이고 한 마디씩 — 과반이 잘못이라 하면 사흘 벌 당번 · 원망은 반으로 풀리고 · 피고는 고발한 이를 원망한다 · 그의 친구는 '나는 그를 믿는다'",
                gathered >= 4 && verdict.Kind == FactKind.Verdict && (int)verdict.Size == 2 && m.Penanced(x, s.Tick) && soc.GrudgeOf(acc, x) < 0.35f && soc.GrudgeOf(x, acc) >= 0.25f
                && lines.Any(l => l.Contains("벌을 받아야")) && lines.Any(l => l.Contains("믿는다")) && story.Contains("고발했다") && !story.Contains("모두의 원망"),   // 게임 글에 개발 숫자(예전 '모두의 원망 1.1')가 없다
                $"모임 {gathered}/5 · 판결 {verdict.Size} · 벌 당번 {m.Penanced(x, s.Tick)} · 고발한 이의 원망 0.60 → {soc.GrudgeOf(acc, x):0.00} · 피고의 원망 {soc.GrudgeOf(x, acc):0.00} · 말 {lines.Count}마디 · {story}");
            var vc = s.Causes[verdict.Cause];
            int culprit = soc.Culprit(s, verdict.Cause, -1);
            Check("회의의 판결 · 회의를 연 일은 사람 사이의 마디(⑨ 사회) — 한 사람이 저지른 일이 아니라서 판결의 사슬에서 '누구 탓'을 찾아도 회의를 연 이가 나오지 않는다",
                vc.Kind == CauseKind.Society && Director.EngineOf(vc.Kind) == 9 && culprit < 0,
                $"판결 마디 {vc.Kind} → 엔진 {Director.EngineOf(vc.Kind)} · 사슬에서 찾은 탓할 이 {(culprit >= 0 ? m.NameOf(culprit) : "없음")}");
        }

        // ── 동조 (Asch 1951): 대표가 먼저 '벌을 받아야 한다'고 하면 · 그를 믿는 이들은 그 쪽으로 기울어 유죄 (4명 중 4) / 대표를 믿지 않으면 둘만 — 무죄 ──
        {
            (int sentence, float trustAcc) Run2(float trustRep)
            {
                var (s, m, soc, p) = World(5, 5);
                int x = p[0], rep = p[1], a2 = p[2], n1 = p[3], n2 = p[4];
                int N = soc.N;
                soc.Grudge[rep * N + x] = 0.6f; soc.Grudge[a2 * N + x] = 0.6f;
                foreach (int j in new[] { n1, n2 }) soc.TrustOf[j * N + rep] = trustRep;
                soc.Rep = rep; soc.RepUntil = s.Tick + Time.Hours(240);
                long f0 = s.Facts.Total;
                m.CallCouncilNow(s);
                Run(s, m, p, 26);
                var v = s.Facts.Since(f0).FirstOrDefault(f => f.Kind == FactKind.Verdict);
                return (v.Kind == FactKind.Verdict ? (int)v.Size : -1, soc.TrustIn(n1, rep));
            }
            var hiT = Task.Run(() => Run2(0.95f)); var lo = Run2(0.1f); var hi = hiT.Result;
            Check("동조: 대표가 먼저 '벌을 받아야 한다'고 하면 그를 믿는 이들이 그 쪽으로 기울어 유죄 · 대표를 믿지 않으면 무죄 (고발한 대표를 덜 믿게 된다)",
                hi.sentence >= 1 && lo.sentence == 0 && lo.trustAcc < 0.1f,
                $"대표를 믿음 0.95: 판결 {hi.sentence} / 0.1: 판결 {lo.sentence} · 무죄 뒤 대표를 믿음 {lo.trustAcc:0.00}");
        }

        // ── 선거: 대표가 없으면 회의를 열어 뽑는다 — 모두가 믿는 이가 뽑히고 배 전체 지도자가 된다 (한 시간 뒤 무리를 다시 찾아도 그대로) ──
        {
            var (s, m, soc, p) = World(5, 7);
            int w = p[3], N = soc.N;
            foreach (int j in p) if (j != w) { soc.TrustOf[j * N + w] = 0.9f; soc.Bond[j * N + w] = 0.3f; }
            soc.Regroup(s);
            long f0 = s.Facts.Total;
            m.CallCouncilNow(s);
            Run(s, m, p, 26);
            var e = s.Facts.Since(f0).FirstOrDefault(f => f.Kind == FactKind.Elected);
            soc.Regroup(s);
            Check("선거: 대표가 없으면 회의를 열어 뽑는다 — 모두가 믿는 이가 뽑히고 배 전체 지도자가 된다 (무리를 다시 찾아도 그대로)",
                e.Kind == FactKind.Elected && e.Actor == w && soc.Rep == w && soc.Head == w,
                $"뽑힌 이 {(e.Actor >= 0 ? m.NameOf(e.Actor) : "-")} ({e.Size:0}표) · 믿던 이 {m.NameOf(w)} · 대표 {soc.Rep} · 지도자 {soc.Head} · {Story(s, FactKind.Elected)}");
        }

        // ── 배급 반란: 배급 중 이틀째 배고프고 주컴퓨터를 덜 믿는 이들은 '배급을 따르지 않는다'를 가결하고 몫을 넘어 먹는다 · 주컴퓨터는 적고 알린다 /
        //    주컴퓨터를 믿고 성실한 이들이면 부결 — 몫을 지킨다 ──
        {
            (int mutiny, double ate, double ration, string said) Run3(bool angry)
            {
                var (s, m, soc, p) = World(4, 9);
                var sm = s.Get<ShipMind>();
                foreach (int a in p) { soc.TrustComputer[a] = angry ? 0.3f : 0.95f; m.SetPersonality(a, 0.5f, angry ? 0.3f : 0.9f, 0.6f, 0.5f, 0.4f); m[a].C.Hunger = 0.8f; }
                double ration = 0.5;
                m.SetRation(ration, 1000, s.Causes.Add(CauseKind.Computer, "식량이 모자라 주컴퓨터가 배급을 시작했다", s.Tick));
                soc.HungrySince(s.Tick - Time.Hours(49));
                long f0 = s.Facts.Total;
                m.CallCouncilNow(s);
                Run(s, m, p, 25, hunger: false);
                var mu = s.Facts.Since(f0).FirstOrDefault(f => f.Kind == FactKind.Mutiny);
                if (Debug) Console.WriteLine($"    반란 {angry}: 회의 {string.Join(" / ", s.Facts.Since(f0).Where(f => f.Kind == FactKind.Meeting).Select(f => s.Causes.Story(f.Cause)))} · 배급 {m.RationFood} · 사람 {string.Join(",", p.Select(a => $"{m[a].C.Goal}@{m.RoomOf(m[a].C.Cell)}"))}");
                foreach (int a in p) m[a].C.Hunger = 0.9f;
                Run(s, m, p, 120, hunger: false);
                double ate = p.Max(a => m.AteToday(a));
                return (mu.Kind == FactKind.Mutiny ? (int)mu.Size : -9, ate, ration, string.Join(" / ", sm.Log.Select(l => l.text).Where(t => t.Contains("배급"))));
            }
            var calmT = Task.Run(() => Run3(false)); var angry = Run3(true); var calm = calmT.Result;
            Check("배급 반란: 이틀째 배고프고 주컴퓨터를 덜 믿는 이들은 '배급을 따르지 않는다'를 가결하고 몫을 넘어 먹는다 · 주컴퓨터는 적고 함장에게 알린다 / 믿고 성실한 이들은 부결 — 몫을 지킨다",
                angry.mutiny >= 3 && angry.ate > angry.ration * 1.5 && angry.said.Contains("따르지 않기로") && calm.mutiny == -1 && calm.ate <= calm.ration + 0.05,
                $"덜 믿음: 찬성 {angry.mutiny} · 하루 몫 {angry.ration:0.0} · 가장 많이 먹은 이 {angry.ate:0.00} · '{angry.said}' / 믿음: 결과 {calm.mutiny} · 가장 많이 먹은 이 {calm.ate:0.00}");
        }

        // ── 흩어짐: 모이는 중에 불이 나면 회의는 흩어진다 (위기가 먼저) ──
        {
            var (s, m, soc, p) = World(5, 11);
            int x = p[0], N = soc.N;
            soc.Grudge[p[1] * N + x] = 0.6f; soc.Grudge[p[2] * N + x] = 0.6f;
            long f0 = s.Facts.Total;
            m.CallCouncilNow(s);
            Run(s, m, p, 5);
            s.Get<Chemistry>().Ignite(At(3, 1), 3e6, s.Causes.Add(CauseKind.Setup, "침실 콘센트에서 불꽃이 튀었다", s.Tick));
            Run(s, m, p, 20);
            bool verdict = s.Facts.Since(f0).Any(f => f.Kind == FactKind.Verdict);
            if (Debug) Console.WriteLine($"    불: 사실 {string.Join(",", s.Facts.Since(f0).Select(f => f.Kind).Distinct())}");
            Check("흩어짐: 모이는 중에 불이 나면 회의는 흩어진다 (위기가 먼저 · 판결 없음)",
                soc.CouncilsBroken == 1 && !verdict && soc.Meeting.Kind == CouncilKind.None,
                $"흩어짐 {soc.CouncilsBroken} · 판결 {verdict} · {Story(s, FactKind.Meeting)}");
        }
    }
}
