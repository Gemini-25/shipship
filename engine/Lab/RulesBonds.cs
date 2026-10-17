using ShipEngine;

namespace Lab;

/// <summary>⑨ 관계 그물 · 대화 · 갈등 사다리 — 함께 넘긴 위기 · 탓 · 이야깃거리 · 사과와 다툼 · 몸싸움(죽음까지) · 평소엔 드묾. docs/DEPTH.md</summary>
public static partial class Program
{
    private static void BondRules()
    {
        if (!Do("관계")) return;
        Console.WriteLine("\n[⑨ 관계 그물 · 대화]");
        var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        // 평소의 하루는 오래 걸리니 먼저 띄워 둔다 (다른 세계 — 결정론 그대로)
        var calm = Task.Run(() =>
        {
            var s = ShipWorld.Crewed("Kestrel", 6, 6, 2);
            s.Run(Time.Days(1));
            return s;
        });

        // ── 함께 위기를 넘긴 둘은 가까워진다 · 그 일을 함께 기억하고 이야기한다 ──
        {
            var s = Worlds.Full(lay, 2);
            var m = s.Get<Mind>(); var so = s.Get<Society>(); var ch = s.Get<Chemistry>();
            int p1 = m.AddPerson(s, At(13, 2)), p2 = m.AddPerson(s, At(14, 4));
            int p3 = m.AddPerson(s, At(3, 2)), p4 = m.AddPerson(s, At(5, 2));
            m.AddRobot(s, At(22, 4), AgentKind.Robot, At(22, 4));
            foreach (int p in new[] { p1, p2, p3, p4 }) { m[p].C.Hunger = 0; m[p].C.Sleepy = 0; }
            s.Run(3);
            ch.Ignite(At(15, 1), 3e6, s.Causes.Add(CauseKind.Story, "의자 밑에서 불씨가 튀었다", s.Tick));
            for (int k = 0; k < Time.Minutes(20) && s.Facts.CountOf(FactKind.Extinguished) == 0; k++) s.Step();
            s.Run(Time.Minutes(2));
            float crisis = so.BondOf(p1, p2), control = so.BondOf(p3, p4);
            // 멀리 있던 둘도 피해 온 사람이 문을 닫는 것을 봤다면 그 불을 기억할 수 있다 — 무게는 함께 넘긴 둘이 더 크다
            float W(int p) => so.Memories.Where(x => x.Owner == p && x.Topic == Topic.Fire).Select(x => x.Weight).DefaultIfEmpty(0).Max();
            bool remembered = W(p1) > 0 && W(p2) > 0 && W(p1) >= W(p3);
            Check("관계: 함께 불을 넘긴 둘은 가까워지고 서로를 더 믿는다 · 그 일을 둘 다 기억한다 (멀리 있던 둘은 그대로)",
                s.Facts.CountOf(FactKind.Extinguished) > 0 && crisis > control + 0.04f && so.TrustIn(p1, p2) > 0.52f && remembered,
                $"정 함께 넘긴 둘 {crisis:0.00} · 멀리 있던 둘 {control:0.00} · 믿음 {so.TrustIn(p1, p2):0.00} · 기억 {remembered}");

            // 이야깃거리: 그 뒤 몇 시간 — 둘은 그 불 이야기를 한다 (한 마디가 남는다)
            for (int k = 0; k < Time.Hours(5); k++)
            {
                s.Step();
                foreach (int p in new[] { p1, p2, p3, p4 }) { m[p].C.Hunger = 0; m[p].C.Sleepy = 0; }
            }
            var fireTalk = so.Sayings.Where(x => x.Topic == Topic.Fire && (x.Speaker == p1 && x.Listener == p2 || x.Speaker == p2 && x.Listener == p1)).ToList();
            string line = fireTalk.Count > 0 ? s.Causes.Text(fireTalk[0].Line) : "";
            if (Debug) foreach (var x in so.Sayings) Console.WriteLine($"    {Time.Clock(x.Tick)} {m.NameOf(x.Speaker)} → {m.NameOf(x.Listener)} [{x.Topic}/{x.End}] {s.Causes.Text(x.Line)}");
            Check("대화: 함께 겪은 불을 두고 이야기한다 — 주제 · 끝 · 한 마디가 남고 · 말에 빈자리가 없다",
                fireTalk.Count > 0 && line.Length > 2 && !line.Contains('{') && so.Talks > 3 && s.Facts.CountOf(FactKind.Talk) == so.Talks,
                $"대화 {so.Talks}번 · 불 이야기 {fireTalk.Count}번 · \"{line}\" · 정 {so.BondOf(p1, p2):0.00}");
        }

        // ── 탓: 사고의 원인 사슬에 사람의 행동이 있으면 다친 이 · 본 이가 그를 탓한다 ──
        {
            var s = Worlds.Full(lay, 3);
            var m = s.Get<Mind>(); var so = s.Get<Society>(); var life = s.Get<Life>();
            int x = m.AddPerson(s, At(3, 2)), y = m.AddPerson(s, At(14, 2)), z = m.AddPerson(s, At(16, 3));
            s.Run(3);
            int c0 = s.Causes.Add(CauseKind.Person, $"{Ko.I(m.NameOf(x))} 조리대를 켜 둔 채 자리를 떴다", s.Tick, At(15, 2), 0, 0, x);
            int c1 = s.Causes.Add(CauseKind.Fire, "불길에 데었다", s.Tick, At(14, 2), c0);
            s.Facts.Emit(s.Tick, FactKind.Hurt, At(14, 2), 0, c1, actor: m[y].C.Body);
            s.Run(2);
            Check("탓: 사고의 원인에 사람이 있으면 다친 이는 그를 원망하고 · 곁에서 본 이도 조금 · 탓한 일이 남는다",
                so.GrudgeOf(y, x) > 0.25f && so.GrudgeOf(z, x) > 0.05f && so.GrudgeOf(z, x) < so.GrudgeOf(y, x) && s.Facts.CountOf(FactKind.Blamed) > 0 && so.TrustIn(y, x) < 0.5f,
                $"원한 다친 이 {so.GrudgeOf(y, x):0.00} · 본 이 {so.GrudgeOf(z, x):0.00} · 믿음 {so.TrustIn(y, x):0.00}");
        }

        // ── 따지면: 다정한 사람은 사과해 앙금이 풀리고 · 거친 사람은 맞서 사이가 나빠진다 ──
        {
            (long argue, long sorry, float bond, float grudge) Confront(float agreeX, float neuroX)
            {
                var s = Worlds.Full(lay, 4);
                var m = s.Get<Mind>(); var so = s.Get<Society>();
                int x = m.AddPerson(s, At(14, 2)), y = m.AddPerson(s, At(15, 3));
                m.SetPersonality(x, 0.5f, 0.5f, 0.6f, agreeX, neuroX);
                m.SetPersonality(y, 0.5f, 0.5f, 0.6f, 0.4f, 0.6f);
                s.Run(2);
                so.Grudge[y * so.N + x] = 0.6f;
                int c0 = s.Causes.Add(CauseKind.Person, $"{Ko.I(m.NameOf(x))} 거르개를 거꾸로 끼웠다", s.Tick, At(14, 2), 0, 0, x);
                so.Remember(s, y, Topic.Blame, x, c0, 0.8f, At(14, 2));
                for (int k = 0; k < Time.Hours(4); k++) { s.Step(); foreach (int p in new[] { x, y }) { m[p].C.Hunger = 0; m[p].C.Sleepy = 0; m[p].C.Lonely = 0.5f; } }
                return (so.Arguments, so.Apologies, so.BondOf(y, x), so.GrudgeOf(y, x));
            }
            var rough = Confront(0.05f, 0.8f); var kind = Confront(0.95f, 0.1f);
            Check("사과와 다툼: 따지면 다정한 이(친화 0.95)는 사과해 앙금이 줄고 · 거친 이(0.05)는 맞서 사이가 나빠진다",
                rough.argue > 0 && rough.sorry == 0 && rough.bond < 0 && kind.sorry > 0 && kind.grudge < 0.6f && kind.grudge < rough.grudge,
                $"거친 이: 다툼 {rough.argue} · 사과 {rough.sorry} · 정 {rough.bond:0.00} · 원한 {rough.grudge:0.00} / 다정한 이: 다툼 {kind.argue} · 사과 {kind.sorry} · 원한 {kind.grudge:0.00}");
        }

        // ── 갈등 사다리 끝: 깊은 원한 · 거친 성격 · 불안 · 긴장 · 화가 겹치면 끝장을 보려 덤빈다 — 아무도 없으면 크게 다치거나 죽고 · 로봇이 곁에 있으면 먼저 말린다 ──
        {
            (float blunt, bool alive, long fights, bool stopped, string said) Feud(bool robot)
            {
                var s = Worlds.Full(lay, 5);
                var m = s.Get<Mind>(); var so = s.Get<Society>(); var life = s.Get<Life>();
                int x = m.AddPerson(s, At(4, 2)), y = m.AddPerson(s, At(6, 2));
                if (robot) m.AddRobot(s, At(8, 5), AgentKind.Robot, At(8, 5));
                m.SetPersonality(x, 0.5f, 0.3f, 0.6f, 0.05f, 0.95f);
                m.SetPersonality(y, 0.5f, 0.5f, 0.6f, 0.5f, 0.3f);
                s.Run(2);
                so.Grudge[x * so.N + y] = 0.95f;
                m[x].C.Stress = 0.9f; m[x].C.Anger = 0.9f;
                for (int k = 0; k < Time.Hours(2); k++)
                {
                    s.Step(); foreach (int p in new[] { x, y }) { m[p].C.Hunger = 0; m[p].C.Sleepy = 0; }
                    if (Debug && k % 25 == 0 && k < 25 * 20) Console.WriteLine($"    싸움{(robot ? "(로봇)" : "")} {k / 25}분 · 덤빈 이 {m.Thought(x)} 칸 {m[x].C.Cell} · 맞는 이 {m.Thought(y)} 칸 {m[y].C.Cell} · 타박 {life[m[y].C.Body].Blunt:0.00} · 피 {life[m[y].C.Body].BloodL:0.0}L · 싸움 {so.Brawls.Count} · 화 {m[x].C.Anger:0.00}");
                }
                bool stopped = so.Sayings.Any(q => !m[q.Speaker].IsPerson && q.Topic == Topic.Fight);
                string said = string.Join(" / ", so.Sayings.Where(q => q.Topic == Topic.Fight).Take(3).Select(q => s.Causes.Text(q.Line)));
                return (life[m[y].C.Body].Blunt, life[m[y].C.Body].Alive != 0, so.Fights, stopped, said);
            }
            var alone = Feud(false); var watched = Feud(true);
            Check("갈등 끝: 깊은 원한에 몰린 이가 끝장을 보려 덤빈다 — 아무도 없으면 크게 다치거나 죽고 · 로봇이 곁에 있으면 먼저 말려 가볍게 끝난다",
                alone.fights > 0 && (alone.blunt > 0.8f || !alone.alive) && watched.fights > 0 && watched.stopped && watched.blunt < alone.blunt * 0.5f && watched.alive,
                $"홀로: 타박 {alone.blunt:0.00} · {(alone.alive ? "살았다" : "죽었다")} / 로봇 곁: 타박 {watched.blunt:0.00} · 말림 {watched.stopped} · \"{watched.said}\"");
        }

        // ── 연출: 사람 이야기도 하이라이트가 된다 — 다툼은 '사람 이야기'로 잡히고 · 잡담만 하는 둘은 하이라이트가 없다 ──
        {
            (List<Highlight> hl, long talks) Watch(bool grudge)
            {
                var s = Worlds.Full(lay, 4);
                var m = s.Get<Mind>(); var so = s.Get<Society>();
                var dir = new Director(s.Layout) { KeepRing = false };
                int x = m.AddPerson(s, At(14, 2)), y = m.AddPerson(s, At(15, 3));
                m.SetPersonality(x, 0.5f, 0.5f, 0.6f, 0.05f, 0.8f);
                m.SetPersonality(y, 0.5f, 0.5f, 0.6f, 0.4f, 0.6f);
                s.Run(2);
                if (grudge)
                {
                    so.Grudge[y * so.N + x] = 0.6f;
                    int c0 = s.Causes.Add(CauseKind.Person, $"{Ko.I(m.NameOf(x))} 거르개를 거꾸로 끼웠다", s.Tick, At(14, 2), 0, 0, x);
                    so.Remember(s, y, Topic.Blame, x, c0, 0.8f, At(14, 2));
                }
                for (int k = 0; k < Time.Hours(4); k++) { s.Step(); dir.Observe(s); foreach (int p in new[] { x, y }) { m[p].C.Hunger = 0; m[p].C.Sleepy = 0; m[p].C.Lonely = 0.5f; } }
                return (dir.Highlights.ToList(), so.Talks);
            }
            var feud = Watch(true); var calmPair = Watch(false);
            var ppl = feud.hl.FirstOrDefault(h => h.People);
            Check("연출: 사람 이야기도 하이라이트가 된다 — 따지다 다툰 일은 '사람 이야기'로 · 제목에 그 일 · 한 마디 · 잡담뿐인 둘은 하이라이트 없음",
                ppl != null && ppl.Title.Contains("탓") && ppl.Line.Length > 0 && !calmPair.hl.Any(h => h.People) && calmPair.talks > 0,
                $"다툰 둘: {(ppl != null ? $"\"{ppl.Title}\" · \"{ppl.Line}\"" : "없음")} / 잡담한 둘: 하이라이트 {calmPair.hl.Count} · 대화 {calmPair.talks}");
        }

        // ── 평소의 하루: 케스트럴 6명 — 말은 자주 나누고 · 몸싸움은 없다 (갈등은 드물게만 끝까지 간다) ──
        {
            var s = calm.Result;
            var so = s.Get<Society>();
            Check("평소의 하루 (케스트럴 6명): 대화는 많고 · 다툼은 드물고 · 몸싸움 · 몰래 방해는 없다",
                so.Talks > 10 && so.Arguments <= 3 && so.Fights == 0 && so.Sabotages == 0,
                $"대화 {so.Talks} · 다툼 {so.Arguments} · 사과 {so.Apologies} · 소문 {so.Rumors} · 몸싸움 {so.Fights} · 방해 {so.Sabotages} · 가까워짐 {so.Friendships}");
        }
    }
}
