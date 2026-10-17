using ShipEngine;

namespace Lab;

/// <summary>⑧ 마음 (바퀴 2) — 하루의 리듬 · 쉬는 시간: 아침형 · 저녁형은 눕는 때가 다르고 · 쉬는 시간에 하는 일은 성격 · 솜씨 · 곁의 설비에서 나온다. docs/DEPTH.md</summary>
public static partial class Program
{
    private static void RhythmRules()
    {
        if (!Do("하루의 리듬")) return;
        Console.WriteLine("\n[⑧ 하루의 리듬 · 쉬는 시간]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";

        // ── 몸의 시계: 똑같이 졸린(0.5 — 낮이면 버티고 잘 시간대면 눕는다) 두 사람 — 아침형은 저녁형보다 몇 시간 일찍 눕는다 ──
        {
            double LieDown(float chrono)
            {
                var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
                var s = Worlds.Full(lay, 3);
                var m = s.Get<Mind>();
                int p = m.AddPerson(s, Worlds.At(lay, 6, 2), -1, Worlds.At(lay, 3, 5));
                m[p].C.Chrono = chrono;
                m[p].C.ShiftHour = 11;                                        // 교대 11시 · 잘 시간대 3시부터 (몸의 시계만큼 앞뒤로)
                for (long k = 0; k < Time.Hours(8); k++)
                {
                    s.Step();
                    ref var c = ref m[p].C;
                    c.Sleepy = 0.5f; c.Hunger = 0; c.Thirst = 0; c.Bored = 0; c.Lonely = 0;
                    if (c.Goal == GoalKind.Sleep) return Time.HourOfDay(s.Tick);
                }
                return -1;
            }
            double early = LieDown(-2), late = LieDown(2);
            if (Debug) Console.WriteLine($"    아침형 {early:0.00}시 · 저녁형 {late:0.00}시");
            Check("몸의 시계: 똑같이 졸린 두 사람 — 아침형(−2시간)은 저녁형(+2시간)보다 서너 시간 일찍 눕는다 (잘 시간대가 몸의 시계만큼 옮겨 간다)",
                early >= 0 && late >= 0 && late - early >= 3 && late - early <= 5,
                $"아침형 {early:0.0}시 · 저녁형 {late:0.0}시에 눕는다");
        }

        // ── 시작한 쉼은 마친다: 조금 지루한(0.32) 이가 작업대에서 만들기 시작하면 — 하는 동안 지루함이 문턱 아래로 풀려도 45분을 채운다 ──
        //    (예전: 지루함이 0.3 아래로 내려가는 순간 쉼이 사라져 우두커니 섰다가 · 몇 분 뒤 다시 쉬러 가고 — 쉼이 한 번도 끝나지 않았다)
        {
            var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
            var s = Worlds.Full(lay, 5);
            var m = s.Get<Mind>();
            int p = m.AddPerson(s, Worlds.At(lay, 12, 8));
            m.SetPersonality(p, 0.2f, 0.9f, 0.3f, 0.5f, 0.3f);
            m[p].C.ShiftHour = 12;                                            // 일 밖의 시간
            m[p].C.SkRepair = 0.8f;
            int flips = 0; GoalKind last = GoalKind.Idle; long done0 = m.HobbyCount.Sum();
            for (long k = 0; k < Time.Minutes(70) && m.HobbyCount.Sum() == done0; k++)
            {
                s.Step();
                ref var c = ref m[p].C;
                c.Sleepy = 0; c.Hunger = 0; c.Thirst = 0; c.Lonely = 0;
                if (k < 3) c.Bored = 0.32f;
                if (c.Goal != last) { if (last == GoalKind.Relax) flips++; last = c.Goal; }
            }
            long finished = m.HobbyCount.Sum() - done0;
            Check("시작한 쉼은 마친다: 조금 지루한 이가 쉬기 시작하면 하는 동안 지루함이 풀려도 45분을 채운다 (깜빡이며 그만두지 않는다)",
                finished == 1 && flips == 0,
                $"마친 쉼 {finished} · 중간에 그만둔 번 {flips} · 지루함 {m[p].C.Bored:0.00} · {m.Thought(p)}");
        }

        // ── 쉬는 시간: 책장 · 놀이 탁자 · 작업대가 있는 배 — 내향적이고 열린 이는 책을 · 외향적인 이는 놀이판을 · 성실하고 손재주 있는 이는 작업대에서 만든다 (만들수록 손재주가 는다) ──
        {
            var legend = Worlds.MindLegend; legend['k'] = "Bookshelf"; legend['g'] = "GameTable";
            var lay = Worlds.Ascii(Worlds.MindShip.Replace("#.........#.............#", "#.........#..k.......g..#"), legend);
            var s = Worlds.Full(lay, 4);
            var m = s.Get<Mind>();
            int reader = m.AddPerson(s, Worlds.At(lay, 13, 3)), player = m.AddPerson(s, Worlds.At(lay, 20, 3)), maker = m.AddPerson(s, Worlds.At(lay, 14, 8));
            m.SetPersonality(reader, 0.95f, 0.3f, 0.05f, 0.5f, 0.3f);
            m.SetPersonality(player, 0.1f, 0.3f, 0.95f, 0.5f, 0.3f);
            m.SetPersonality(maker, 0.1f, 0.95f, 0.3f, 0.5f, 0.3f);
            s.Run(1);
            m[maker].C.SkRepair = 0.6f;
            var who = new[] { reader, player, maker };
            var picks = who.Select(_ => new Dictionary<Hobby, int>()).ToArray();
            var seen = who.Select(_ => new HashSet<long>()).ToArray();
            for (long k = 0; k < Time.Hours(16); k++)
            {
                s.Step();
                for (int i = 0; i < who.Length; i++)
                {
                    ref var c = ref m[who[i]].C;
                    c.Sleepy = 0; c.Hunger = 0; c.Thirst = 0; c.Lonely = 0; c.Bored = Math.Max(c.Bored, 0.8f);
                    if (c.Goal == GoalKind.Relax && seen[i].Add(c.GoalSince)) picks[i][(Hobby)c.Hobby] = picks[i].GetValueOrDefault((Hobby)c.Hobby) + 1;
                }
            }
            float Share(int i, Hobby h) { int n = picks[i].Values.Sum(); return n == 0 ? 0 : picks[i].GetValueOrDefault(h) / (float)n; }
            bool Most(int i, Hobby h) => Enumerable.Range(0, 3).All(j => j == i || Share(i, h) > Share(j, h)); // 그 일을 하는 몫이 셋 중 가장 크다
            string Show(int i) => string.Join(" ", picks[i].OrderByDescending(kv => kv.Value).Select(kv => $"{Mind.HobbyName(kv.Key)} {kv.Value}"));
            if (Debug) for (int i = 0; i < 3; i++) Console.WriteLine($"    {i}: {Show(i)}");
            float skill = m[maker].C.SkRepair;
            Check("쉬는 시간: 내향적이고 열린 이는 책을 · 외향적인 이는 놀이판을 · 성실하고 손재주 있는 이는 작업대에서 만드는 몫이 가장 크다 — 만들수록 손재주가 는다",
                Most(0, Hobby.Read) && Most(1, Hobby.Game) && Most(2, Hobby.Tinker) && Share(0, Hobby.Read) > 0.5f && Share(1, Hobby.Game) > 0.5f && skill > 0.6f + 0.004f,
                $"읽는 이: {Show(0)} / 노는 이: {Show(1)} / 만드는 이: {Show(2)} · 손재주 0.60 → {skill:0.000}");
        }
    }
}
