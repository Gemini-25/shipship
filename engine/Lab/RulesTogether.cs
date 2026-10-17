using ShipEngine;

namespace Lab;

/// <summary>⑨ 보강 (바퀴 2) — 사회 값이 일상 행동에 닿는다: 함께 먹고 · 아는 이를 따라 피하고 · 다친 짝에게 달려가고 · 옆 방 비명에 온다. docs/DEPTH.md</summary>
public static partial class Program
{
    private static void TogetherRules()
    {
        if (!Do("함께")) return;
        Console.WriteLine("\n[⑨ 보강 — 함께]");
        var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        void Pair(Society so, int a, int b) { so.Partner[a] = b; so.Partner[b] = a; so.Bond[a * so.N + b] = so.Bond[b * so.N + a] = 0.7f; }

        // ── 함께 먹는다: 배고픈 짝이 먹으러 가면 조금(0.45) 배고픈 이도 따라가 같은 배식기에서 먹는다 (자기 곁 배식기가 아니라) — 남남이면 아직 먹지 않는다 ──
        {
            (bool ate, bool same) Run(bool mates)
            {
                var s = Worlds.Full(lay, 2);
                var m = s.Get<Mind>(); var so = s.Get<Society>();
                int a = m.AddPerson(s, At(3, 8)), b = m.AddPerson(s, At(18, 8));     // a는 왼쪽 배식기 곁 · b는 오른쪽 배식기 곁
                s.Run(1);
                if (mates) Pair(so, a, b);
                m[a].C.Hunger = 0.85f; m[b].C.Hunger = 0.45f;
                long meals0 = m.Meals; int bSrc = -1;
                for (int k = 0; k < Time.Minutes(25); k++)
                {
                    s.Step();
                    foreach (int p in new[] { a, b }) { m[p].C.Sleepy = 0; m[p].C.Thirst = 0; m[p].C.Bored = 0; m[p].C.Lonely = 0; }
                    if (m[b].C.Goal == GoalKind.Eat) foreach (var st in m[b].Plan) if (st.Kind == StepKind.Use && (UseKind)st.Arg == UseKind.Eat) bSrc = st.Target;
                }
                int aSrc = lay.Furniture.Where(f => f.Type == "MealDispenser").OrderBy(f => m.Dist(At(3, 8), f.Cells[0])).First().Id;
                if (Debug) Console.WriteLine($"    {(mates ? "짝" : "남남")}: 끼니 {m.Meals - meals0} · b 배고픔 {m[b].C.Hunger:0.00} · b 자리 {bSrc} (a 자리 {aSrc}) · 함께 {m.AteTogether}");
                return (m[b].C.Hunger < 0.2f, bSrc == aSrc);
            }
            var mates = Run(true); var strangers = Run(false);
            Check("함께 먹는다: 배고픈 짝이 먹으러 가면 조금 배고픈 이도 따라가 같은 배식기에서 먹는다 (제 곁 배식기가 아니라) — 남남이면 아직 먹지 않는다",
                mates.ate && mates.same && !strangers.ate,
                $"짝: 먹음 {mates.ate} · 같은 자리 {mates.same} / 남남: 먹음 {strangers.ate}");
        }

        // ── 따라 나간다: 주컴퓨터 화면으로 '공기가 나쁘다'를 본 이가 방을 빠져나가면 — 짝은 위험을 몰라도 따라 나간다 · 남남은 남는다 ──
        {
            (bool left, bool knew) Run(bool mates)
            {
                var s = Worlds.Full(lay, 4);
                var m = s.Get<Mind>(); var so = s.Get<Society>();
                int a = m.AddPerson(s, At(4, 2)), b = m.AddPerson(s, At(6, 3));
                s.Run(1);
                if (mates) Pair(so, a, b);
                int room = lay.Room[At(4, 2)];
                m.Learn(m[a], s.Tick, BeliefKind.BadAir, At(2, 1), -1, 1f, BeliefSource.Screen);
                bool knew = false;
                for (int k = 0; k < Time.Minutes(3); k++)
                {
                    s.Step();
                    foreach (int p in new[] { a, b }) { m[p].C.Sleepy = 0; m[p].C.Hunger = 0; m[p].C.Thirst = 0; m[p].C.Bored = 0; }
                    knew |= m[b].Beliefs.Any(x => x.Kind == BeliefKind.BadAir && x.Conf > 0.3f);
                }
                if (Debug) Console.WriteLine($"    {(mates ? "짝" : "남남")}: a {m.Thought(a)} 방 {m.RoomOf(m[a].C.Cell)} · b {m.Thought(b)} 방 {m.RoomOf(m[b].C.Cell)} (나쁜 방 {room}) · 따라 나감 {m.Followed} · b가 알았나 {knew}");
                return (m.RoomOf(m[b].C.Cell) != room, knew);
            }
            var mates = Run(true); var strangers = Run(false);
            Check("따라 나간다: 나쁜 공기를 안 이가 방을 빠져나가면 짝은 위험을 몰라도 따라 나간다 · 남남은 남는다 (피난 연구 — 아는 이와 함께 움직인다)",
                mates.left && !mates.knew && !strangers.left && !strangers.knew,
                $"짝: 나감 {mates.left} (위험을 알았나 {mates.knew}) / 남남: 나감 {strangers.left}");
        }

        // ── 되풀이하지 않는다: 따라 나선 걸음이 끊겨도 (막힌 문 · 길) 10분 동안은 다시 따라 나서지 않는다
        //    (디딤호에서: 넓은 둘레 통로에서 걸음이 거듭 끊겨 한 사람이 같은 지도자를 18분에 13번 따라 나섰다) — 넓은 오른쪽 방 끝에서 · 따라 나설 때마다 걸음을 끊는다 ──
        {
            var s = Worlds.Full(lay, 4);
            var m = s.Get<Mind>(); var so = s.Get<Society>();
            int a = m.AddPerson(s, At(22, 2)), b = m.AddPerson(s, At(21, 3));
            s.Run(1);
            Pair(so, a, b);
            m.Learn(m[a], s.Tick, BeliefKind.BadAir, At(20, 1), -1, 1f, BeliefSource.Screen);
            long seen = 0; bool leaderOut = false;
            for (int k = 0; k < Time.Minutes(2); k++)
            {
                s.Step();
                foreach (int p in new[] { a, b }) { m[p].C.Sleepy = 0; m[p].C.Hunger = 0; m[p].C.Thirst = 0; m[p].C.Bored = 0; }
                if (m.Followed != seen) { seen = m.Followed; m[b].Plan.Clear(); m[b].Path.Clear(); m[b].C.StepIndex = 0; m[b].C.Goal = GoalKind.Idle; } // 걸음이 끊겼다
                leaderOut |= m.RoomOf(m[a].C.Cell) != lay.Room[At(22, 2)];
            }
            Check("되풀이하지 않는다: 따라 나선 걸음이 끊겨도 10분 동안은 다시 따라 나서지 않는다 (넓은 통로에서 같은 사람을 거듭 따라 나서던 결함)",
                leaderOut && m.Followed == 1, $"따라 나섬 {m.Followed}번 · 앞사람 나감 {leaderOut}");
        }

        // ── 다친 짝: 산소 없는 방에 쓰러진 이 — 곁의 남이 먼저 구하러 갔어도 짝은 달려간다 · 짝이 아니면 맡은 이에게 맡긴다 ──
        {
            bool Run(bool mates)
            {
                var s = Worlds.Full(lay, 6);
                var m = s.Get<Mind>(); var f = s.Get<Field>(); var life = s.Get<Life>(); var so = s.Get<Society>();
                int v = m.AddPerson(s, At(6, 4));
                int q = m.AddPerson(s, At(12, 7)), p = m.AddPerson(s, At(14, 7));    // q가 먼저 고른다 (번호가 앞) · 둘 다 통로
                s.Run(1);
                if (mates) Pair(so, p, v);
                foreach (int c in lay.Rooms[lay.Room[At(6, 2)]].Cells) { double o2 = f.Gas(f.T.gO2, c); f.RemoveGas(c, f.T.gO2, o2); f.AddGas(c, f.T.gN2, o2, 293); }
                ref var vb = ref life[m[v].C.Body];
                vb.Conscious = 0; vb.Hypoxia = 0.8f;
                foreach (int x in new[] { q, p })
                {
                    m.SetPersonality(x, 0.5f, 0.5f, 0.5f, 0.6f, 0.3f);
                    m[x].C.Brave = 0.9f; m[x].C.SkMedic = 0.5f;
                    m.Learn(m[x], s.Tick, BeliefKind.Injured, m[v].C.Cell, m[v].C.Body, 1f, BeliefSource.Told);
                }
                bool qWent = false, pWent = false;
                for (int k = 0; k < Time.Minutes(1); k++)
                {
                    s.Step();
                    qWent |= m[q].C.Goal == GoalKind.Rescue; pWent |= m[p].C.Goal == GoalKind.Rescue;
                }
                if (Debug) Console.WriteLine($"    {(mates ? "짝" : "남남")}: 남 {m.Thought(q)} ({qWent}) · {(mates ? "짝" : "다른 이")} {m.Thought(p)} ({pWent})");
                return qWent && pWent;
            }
            bool mates = Run(true), strangers = Run(false);
            Check("다친 짝: 쓰러진 이를 곁의 남이 먼저 구하러 갔어도 짝은 달려간다 · 짝이 아니면 맡은 이에게 맡긴다",
                mates && !strangers, $"짝: 둘 다 감 {mates} / 남남: 둘 다 감 {strangers}");
        }

        // ── 옆 방 비명: 통로에서 끝장을 보려는 몸싸움 — 닫힌 문 너머 방의 로봇이 맞는 이의 비명을 듣고 와서 말린다 (로봇이 없으면 크게 다치거나 죽는다) ──
        {
            (float blunt, bool alive, bool stopped) Feud(bool robot)
            {
                var s = Worlds.Full(lay, 5);
                var m = s.Get<Mind>(); var so = s.Get<Society>(); var life = s.Get<Life>();
                int x = m.AddPerson(s, At(12, 8)), y = m.AddPerson(s, At(15, 8));
                if (robot) m.AddRobot(s, At(3, 3), AgentKind.Robot, At(3, 3));      // 왼쪽 방 (문은 닫혀 있다)
                m.SetPersonality(x, 0.5f, 0.3f, 0.6f, 0.05f, 0.95f);
                m.SetPersonality(y, 0.5f, 0.5f, 0.6f, 0.5f, 0.3f);
                s.Run(2);
                so.Grudge[x * so.N + y] = 0.95f;
                m[x].C.Stress = 0.9f; m[x].C.Anger = 0.9f;
                for (int k = 0; k < Time.Minutes(30); k++)
                {
                    s.Step(); foreach (int p in new[] { x, y }) { m[p].C.Hunger = 0; m[p].C.Sleepy = 0; }
                    if (Debug && robot && k % 10 == 0 && k < 25 * 6) Console.WriteLine($"    {k / 25}분 로봇 {m.Thought(2)} 칸 ({lay.X(m[2].C.Cell)},{lay.Y(m[2].C.Cell)}) 믿음 {string.Join(",", m[2].Beliefs.Select(b => $"{b.Kind}:{b.Conf:0.0}"))} 계획 {string.Join(">", m[2].Plan.Select(p => p.Kind.ToString()))} 기다림 {m[2].C.WaitDoor} · 맞는 이 {m.Thought(y)} 타박 {life[m[y].C.Body].Blunt:0.00} · 싸움 {so.Brawls.Count}");
                }
                bool stopped = so.Sayings.Any(q => !m[q.Speaker].IsPerson && q.Topic == Topic.Fight);
                return (life[m[y].C.Body].Blunt, life[m[y].C.Body].Alive != 0, stopped);
            }
            var alone = Feud(false); var heard = Feud(true);
            Check("옆 방 비명: 통로의 끝장 싸움 — 닫힌 문 너머 로봇이 맞는 이의 비명을 듣고 와서 말린다 → 산다 (로봇이 없으면 크게 다치거나 죽는다)",
                (alone.blunt > 0.8f || !alone.alive) && heard.stopped && heard.alive && heard.blunt < alone.blunt,
                $"홀로: 타박 {alone.blunt:0.00} · {(alone.alive ? "살았다" : "죽었다")} / 옆 방 로봇: 말림 {heard.stopped} · 타박 {heard.blunt:0.00} · {(heard.alive ? "살았다" : "죽었다")}");
        }
    }
}
