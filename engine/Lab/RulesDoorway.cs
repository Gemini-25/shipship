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

        // (라) C3 파편 대피: 해치 대각선 충전독 (12,1)에서 충전하는 로봇 (전지 0.3) — 12분 뒤 열어 둔 해치가 닫힌다 · 로봇은 독에 그대로 · 사람 둘은 대피실 (예전: 곁 한 칸의 깨어 있는 로봇 때문에 해치를 끝내 닫지 않았다)
        {
            var wl = new Dictionary<char, string> { ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer", ['e'] = "Bed", ['R'] = "SensorArray", ['r'] = "RobotDock" };
            var wlay = Worlds.Ascii(WeatherShip.Replace("#..e.......#..R....#", "#..e.......#r.R....#"), wl);
            int WAt(int x, int y) => Worlds.At(wlay, x, y);
            var s = Worlds.Full(wlay, 7);
            var m = s.Get<Mind>(); var o = s.Get<Outside>(); var f = s.Get<Field>(); var soc = s.Get<Society>(); var net = s.Get<Network>();
            int a = m.AddPerson(s, WAt(3, 1)), b = m.AddPerson(s, WAt(3, 3));
            int r = m.AddRobot(s, WAt(12, 1), AgentKind.Robot, WAt(12, 1));
            s.Run(5);
            foreach (int p in new[] { a, b }) { soc.TrustComputer[p] = 0.9f; m[p].C.Diligent = 0.8f; }
            net.Robot(m[r].C.Mech).Battery = 0.3f;
            int hatch = wlay.DoorAt[WAt(11, 2)];
            f.SetDoor(hatch, 1); s.Get<Structure>().RequestDoor(hatch, 1);
            o.AddCloud("kessler", 10000 * 12 * 60 + 400_000, 400_000, 0, 1, 0, s.Causes.Add(CauseKind.Setup, "파편 폭풍이 항로로 들어온다", s.Tick));
            for (int k = 0; k < Time.Minutes(12); k++) { net.Robot(m[r].C.Mech).Battery = Math.Min(net.Robot(m[r].C.Mech).Battery, 0.3f); s.Step(); }
            int right = wlay.Room[WAt(25, 2)];
            bool gathered = new[] { a, b }.All(p => m.RoomOf(m[p].C.Cell) == right);
            Check("문 칸 (C3) 파편 대피: 해치 대각선 독에서 충전하는 로봇이 있어도 12분 뒤 열어 둔 해치가 닫힌다 · 로봇은 독에 그대로 · 사람 둘은 대피실",
                f.DoorOpen[hatch] < 0.01f && m[r].C.Cell == WAt(12, 1) && gathered,
                $"해치 열림 {f.DoorOpen[hatch]:0.00} · 로봇 칸 ({wlay.X(m[r].C.Cell)},{wlay.Y(m[r].C.Cell)}) · 모임 {gathered}");
        }

        // (마) C3 봉쇄: 불난 방의 문 칸에 몸이 누워 있으면 그 문은 쓰는 중 — 몸이 있는 동안 주컴퓨터의 닫기 요청이 하나를 넘지 않는다 (예전: 10틱마다 되풀이)
        {
            var clay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
            int CAt(int x, int y) => Worlds.At(clay, x, y);
            var s = Worlds.Full(clay, 1);
            var f = s.Get<Field>(); var ch = s.Get<Chemistry>(); var sm = s.Get<ShipMind>(); var net = s.Get<Network>(); var life = s.Get<Life>();
            s.Run(3);
            int room = clay.Room[CAt(15, 3)];
            int sdoor = s.Get<Structure>().RoomDoors(room).First(d => !clay.Doors[d].External);
            int dc = clay.Doors[sdoor].Cell;
            f.SetDoor(sdoor, 1); s.Get<Structure>().RequestDoor(sdoor, 1);
            int body = life.AddBody(dc);
            int bed = clay.Rooms[room].Cells.Where(c => net.LineAt[c] == 0 && clay.FurnitureAt[c] < 0).OrderBy(c => Math.Abs(clay.X(c) - 15) + Math.Abs(clay.Y(c) - 3)).First();
            f.AddStore(bed, f.T.sPu, 300);
            ch.Ignite(bed, 3e6, s.Causes.Add(CauseKind.Setup, "폼 침대에 불", s.Tick));
            long req0 = sm.SealDoorRequests;
            for (int k = 0; k < Time.Minutes(4); k++) { life[body].Cell = dc; s.Step(); }
            int seals = s.Facts.Since(0).Count(x => x.Kind == FactKind.ComputerSeal && x.Other == room);
            Check("문 칸 (C3) 봉쇄: 불난 방의 문 칸에 몸이 누워 있는 동안 주컴퓨터가 그 문에 닫기를 하나 넘게 요청하지 않는다 (봉쇄는 한다)",
                seals == 1 && sm.SealDoorRequests - req0 <= 1, $"봉쇄 {seals} · 닫기 요청 {sm.SealDoorRequests - req0}");
        }

        // (바) C4 피할 곳은 문 바로 앞이 아닌 곳: 침실 (5,1)의 한 사람 · 침실 왼쪽 외판 구멍 0.003m² — 피해 나와 처음 선 칸이 문에서 2칸 넘게 (문 곁이 아니다) · 그때 문이 닫혔고 · 10분 뒤 산다
        //     (예전: 구멍에서 4칸 넘게 떨어진 처음 칸 — 문 곁 (5,4)에 섰다)
        {
            var s = Worlds.Full(lay, 3);
            var m = s.Get<Mind>(); var f = s.Get<Field>(); var life = s.Get<Life>();
            int p = m.AddPerson(s, At(5, 1));
            s.Run(2);
            f.SetHole(At(0, 1), 0.003f);   // 침실 왼쪽 외판 (구멍에서 4칸 넘게 떨어진 처음 칸이 문 곁 (5,4)이 되게)
            int first = -1; float doorAt = 1;
            for (int k = 0; k < (int)Time.Minutes(10); k++)
            {
                ref var c = ref m[p].C; c.Sleepy = 0; c.Hunger = 0; c.Thirst = 0; s.Step();
                ref var c2 = ref m[p].C;
                if (first < 0 && c2.Goal == GoalKind.Evacuate && m[p].Plan.Count > 0 && m[p].Plan[0].Kind == StepKind.GoTo && c2.Cell == m[p].Plan[0].Cell) { first = c2.Cell; s.Run(60); doorAt = f.DoorOpen[door]; }
            }
            Check("문 칸 (C4) 피할 곳: 침실 외판에 작은 구멍 — 피해 나와 처음 선 칸이 식당 · 문에서 2칸 넘게 (문 곁이 아니다) · 그 뒤 문이 닫혔고 · 10분 뒤 산다",
                first >= 0 && lay.Room[first] == lay.Room[At(5, 5)] && m.Dist(first, At(5, 3)) >= 2 && doorAt <= 0.01f && life[m[p].C.Body].Alive != 0,
                $"처음 선 칸 {(first >= 0 ? $"({lay.X(first) - 1},{lay.Y(first) - 1}) · 문까지 {m.Dist(first, At(5, 3))}" : "없음")} · 그 뒤 문 열림 {doorAt:0.00} · 산다 {life[m[p].C.Body].Alive != 0}");
        }

        // (사) C5 줄 서는 자리: WashShip (변기 T(8,1)이 문 D(7,2)의 대각선 · 먼 변기는 흡입 팬이 망가졌다) — A (대변 0.75)가 앉은 뒤 B (방광 0.8)가 온다
        //     줄 선 동안 B의 칸이 문 칸 · 문 곁이 아니다 · 그동안 문 닫기를 요청하면 1분 안에 닫힌다 · A가 나온 뒤 B가 쓰고 지림 0 (예전: 문 칸 (7,2)에서 줄을 서 문이 열린 채)
        {
            var wl = Worlds.Ascii(WashShip, new Dictionary<char, string> { ['e'] = "Bed", ['T'] = "Toilet", ['S'] = "Shower", ['k'] = "Sink", ['p'] = "PowerPanel", ['b'] = "Battery" });
            int WAt(int x, int y) => Worlds.At(wl, x, y);
            var s = Worlds.Full(wl, 5);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>(); var st = s.Get<Structure>(); var f = s.Get<Field>();
            int pa = m.AddPerson(s, WAt(11, 2)), pb = m.AddPerson(s, WAt(2, 2));
            s.Run(2);
            var toilets = wl.Furniture.Where(x => x.Type == "Toilet").OrderBy(x => x.Cells[0] % wl.W).ToList();
            net.Broken[net.Devices.First(d => d.Furniture == toilets[1].Id).Id * 4 + (int)Part.Mechanical] = 1;   // 먼 변기는 흡입 팬이 망가졌다
            int seat = toilets[0].Cells[0], wdoor = wl.DoorAt[WAt(7, 2)];
            void Calm(int p) { int off = ((int)Time.HourOfDay(s.Tick) + 24 - 10) % 24; ref var c = ref m[p].C; c.ShiftHour = off; c.Sleepy = 0; c.Hunger = 0; c.Thirst = 0; c.Lonely = 0; c.Unfit = 0; c.Bored = 0; }
            life[m[pa].C.Body].Bowel = Life.BowelMax * 0.75;
            for (long t = 0; t < Time.Minutes(10) && m[pa].C.Cell != seat; t++) { Calm(pa); Calm(pb); s.Step(); }
            life[m[pb].C.Body].Bladder = Life.BladderMax * 0.8;
            int queued = 0, nearDoor = 0, worst = 0; float shut = -1;
            for (long t = 0; t < Time.Minutes(30) && m.Toilets < 2; t++)
            {
                Calm(pa); Calm(pb); s.Step();
                var b = m[pb];
                bool inQueue = b.C.Goal == GoalKind.Toilet && b.C.StepIndex < b.Plan.Count && b.Plan[b.C.StepIndex].Kind == StepKind.Use && (UseKind)b.Plan[b.C.StepIndex].Arg == UseKind.Queue && b.C.StepIndex > 0;
                if (!inQueue) continue;
                queued++;
                int k = m.StandKindAt(b.C.Cell); worst = Math.Max(worst, k);
                if (k >= 2) nearDoor++;
                if (queued == 5) st.RequestDoor(wdoor, 0);
                if (queued == 5 + 60) shut = f.DoorOpen[wdoor];
            }
            Check("문 칸 (C5) 줄 서는 자리: 문 대각선의 변기 — 줄 선 동안 기다리는 이의 칸이 문 칸 · 문 곁이 아니다 · 그동안 문 닫기를 요청하면 1분 안에 닫힌다 · 앞사람이 나온 뒤 쓰고 지림 0",
                queued >= 65 && nearDoor == 0 && shut >= 0 && shut <= 0.01f && m.Toilets == 2 && life.Soilings == 0,
                $"줄 {queued}틱 · 문 곁 {nearDoor}틱 (가장 나쁜 표 {worst}) · 1분 뒤 문 열림 {shut:0.00} · 비움 {m.Toilets} · 지림 {life.Soilings}");
        }
    }
}
