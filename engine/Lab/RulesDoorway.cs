using ShipEngine;

namespace Lab;

/// <summary>규칙 시험 — 5단 '문 칸' (docs/STAGE5.md C1 ~): 머물 자리는 문 칸 · 문 곁 · 남이 선 칸이 아닌 곳.</summary>
public static partial class Program
{
    private const string DoorwayShip = @"
###########
#e.e.e.epb#
#.........#
#####D#####
#.........#
#m........#
###########";

    private static void DoorwayRules()
    {
        if (!Do("문 칸")) return;
        Console.WriteLine("\n[문 칸 — 회의 · 대피 자리 (C1)]");
        var legend = new Dictionary<char, string> { ['e'] = "Bed", ['p'] = "PowerPanel", ['b'] = "Battery", ['m'] = "MealDispenser" };
        var lay = Worlds.Ascii(DoorwayShip, legend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        int door = lay.DoorAt[At(5, 3)];

        (Sim s, Mind m, Society soc, List<int> p) World(ulong seed)
        {
            var s = Worlds.Full(lay, seed);
            var m = s.Get<Mind>(); var soc = s.Get<Society>();
            var p = new List<int>();
            int[] xs = { 2, 4, 6, 2, 4 }, ys = { 1, 1, 1, 2, 2 };
            for (int i = 0; i < 5; i++) { int a = m.AddPerson(s, At(xs[i], ys[i])); m.SetPersonality(a, 0.5f, 0.9f, 0.6f, 0.5f, 0.4f); p.Add(a); }
            s.Run(2);
            return (s, m, soc, p);
        }
        void Keep(Sim s, Mind m, List<int> p, int sleepy = -1) { foreach (int a in p) { ref var c = ref m[a].C; c.Sleepy = a == sleepy ? 0.95f : 0; c.Thirst = 0; c.Hunger = 0; if (a != sleepy) s.Get<Life>()[c.Body].Asleep = 0; } }
        // 모임 · 대피에 머무는 이들의 자리: 문 칸 · 문 곁에 선 사람 수 · 같은 칸에 둘 (한 번이라도)
        (int stay, int atDoor, int shared) Look(Mind m, List<int> p, GoalKind goal)
        {
            var on = p.Where(a => m[a].C.Goal == goal && m[a].C.StepIndex < m[a].Plan.Count && m[a].Plan[m[a].C.StepIndex].Kind == StepKind.Use).Select(a => m[a].C.Cell).ToList();
            return (on.Count, on.Count(c => m.StandKindAt(c) >= 2), on.Count - on.Distinct().Count());
        }

        // (가) 회의: 원망 셋 → 회의 — 14분 + 12분 뒤 모임에 머무는 이 넷 넘게 · 문 칸 · 문 곁 0 · 같은 칸 둘 0 (내내) · 문을 닫으라 하면 1분 안에 닫힌다
        {
            var (s, m, soc, p) = World(3);
            int N = soc.N;
            soc.Grudge[p[1] * N + p[0]] = 0.6f; soc.Grudge[p[2] * N + p[0]] = 0.6f; soc.Grudge[p[3] * N + p[0]] = 0.4f;
            m.CallCouncilNow(s);
            int shared = 0, door2 = 0, best = 0;
            for (int k = 0; k < (int)Time.Minutes(26); k++)
            {
                s.Step(); Keep(s, m, p);
                var l = Look(m, p, GoalKind.Meeting);
                if (k >= (int)Time.Minutes(14)) { shared += l.shared; door2 += l.atDoor; best = Math.Max(best, l.stay); }
            }
            s.Get<Structure>().RequestDoor(door, 0);
            s.Run(25);
            var f = s.Get<Field>();
            Check("문 칸 (C1) 회의: 식당에 모여 머무는 이 넷 넘게 · 그 가운데 문 칸 · 문 곁 (대각선 한 칸 안)에 선 이 0 · 같은 칸에 둘 0 · 문을 닫으라 하면 1분 안에 닫힌다",
                best >= 4 && door2 == 0 && shared == 0 && f.DoorOpen[door] == 0,
                $"머무는 이 {best} · 문 곁 사람-틱 {door2} · 같은 칸 {shared} · 문 열림 {f.DoorOpen[door]:0.00}");
        }

        // (나) 대피: 주컴퓨터를 믿는 (0.9) 성실한 다섯 · 한 명은 졸림 0.95 — 식당으로 대피 2시간 → 10분 뒤 같은 셋 · 졸린 이는 표 0 바닥에서 잔다
        {
            var (s, m, soc, p) = World(5);
            foreach (int a in p) { soc.TrustComputer[a] = 0.9f; m.SetPersonality(a, 0.5f, 1f, 0.6f, 0.5f, 0.4f); }
            int sleepy = p[4], mess = lay.Room[At(5, 5)];
            m.Shelter(s, mess, At(5, 5), s.Tick + Time.Hours(2), s.Causes.Add(CauseKind.Setup, "시험: 파편 구름 — 식당으로 대피", s.Tick), 1);
            int shared = 0, door2 = 0, best = 0;
            for (int k = 0; k < (int)Time.Minutes(10); k++)
            {
                s.Step(); Keep(s, m, p, sleepy);
                var l = Look(m, p, GoalKind.Shelter);
                if (k >= (int)Time.Minutes(6)) { shared += l.shared; door2 += l.atDoor; best = Math.Max(best, l.stay); }
            }
            var life = s.Get<Life>();
            int sc = m[sleepy].C.Cell;
            bool slept = life[m[sleepy].C.Body].Asleep != 0 && lay.Room[sc] == mess && m.StandKindAt(sc) == 0;
            Check("문 칸 (C1) 대피: 식당으로 대피한 이들 — 머무는 이 셋 넘게 · 문 칸 · 문 곁 0 · 같은 칸 둘 0 · 졸린 이는 식당의 표 0 바닥 (문 곁 아님)에서 잔다",
                best >= 3 && door2 == 0 && shared == 0 && slept,
                $"머무는 이 {best} · 문 곁 사람-틱 {door2} · 같은 칸 {shared} · 졸린 이 칸 ({lay.X(sc)},{lay.Y(sc)}) 표 {m.StandKindAt(sc)} · 잠 {life[m[sleepy].C.Body].Asleep}");
        }

        // (다) C2 연 문은 서서도 챙긴다: 배식기가 문 바로 아래 (5,5) — 배고픈 이가 침실에서 와 (5,4)에서 먹는다 → 2분 안에 문이 닫히고 먹기는 끝까지 (예전: 먹는 10분 내내 열려 있었다)
        {
            var lay2 = Worlds.Ascii(DoorwayShip.Replace("#m........#", "#....m....#"), legend);
            var s = Worlds.Full(lay2, 3);
            var m = s.Get<Mind>(); var f = s.Get<Field>();
            int p = m.AddPerson(s, Worlds.At(lay2, 4, 1));
            s.Run(2);
            int d2 = lay2.DoorAt[Worlds.At(lay2, 5, 3)];
            long meals0 = m.Meals, startEat = -1, closedAt = -1;
            for (int k = 0; k < (int)Time.Minutes(30) && m.Meals == meals0; k++)
            {
                ref var c = ref m[p].C; c.Hunger = Math.Max(c.Hunger, 0.9f); c.Sleepy = 0; c.Thirst = 0;
                s.Step();
                bool eating = m[p].C.Goal == GoalKind.Eat && m[p].C.StepIndex < m[p].Plan.Count && m[p].Plan[m[p].C.StepIndex].Kind == StepKind.Use;
                if (eating && startEat < 0) startEat = s.Tick;
                if (startEat >= 0 && closedAt < 0 && f.DoorOpen[d2] <= 0.01f) closedAt = s.Tick;
            }
            Check("문 칸 (C2) 연 문은 서서도 챙긴다: 문 바로 아래 배식기에서 먹는 이 — 먹기 시작한 뒤 2분 안에 제가 연 문이 닫히고 먹기는 끝까지 간다",
                startEat >= 0 && closedAt >= 0 && closedAt - startEat <= Time.Minutes(2) && m.Meals == meals0 + 1,
                $"먹기 시작 {startEat} · 문 닫힘 {closedAt} ({(closedAt >= 0 && startEat >= 0 ? (closedAt - startEat) * Time.SecondsPerTick / 60 : -1):0.0}분) · 먹음 {m.Meals - meals0}");
        }
    }
}
