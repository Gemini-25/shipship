using ShipEngine;

namespace Lab;

/// <summary>⑥ 망 바퀴 2 — 고쳐 쓰기: 연장선(젖은 급전선 · 문틀 · 합선 · 봉쇄) · 로봇 부품 주머니 · 떼어 쓰기(헌 부품) · 물 재생기 봉함 샘. docs/ENGINE.md ⑥</summary>
public static partial class Program
{
    /// <summary>왼쪽 생명유지실(배전반 p · 산소 발생기 O · 커피 기계 k · 물 재생기 W · 의자 s) | 문 D | 오른쪽 방(배전반 q · 배터리 b · 주컴퓨터 c · 충전독 r · 작업대 w) · 의자 s는 왼쪽.</summary>
    private const string NetShip = @"
##################
#p..O..k...#c...b#
#..W.......D..q..#
#.......s..#r...w#
##################";

    private static Dictionary<char, string> NetLegend => new()
    {
        ['p'] = "PowerPanel", ['q'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer", ['O'] = "OxygenGenerator", ['k'] = "CoffeeMachine",
        ['W'] = "WaterRecycler", ['r'] = "RobotDock", ['w'] = "Workbench", ['s'] = "Seat",
    };

    private static void NetRules()
    {
        if (!Do("고쳐 쓰기")) return;
        Console.WriteLine("\n[⑥ 고쳐 쓰기 · 연장선]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var lay = Worlds.Ascii(NetShip, NetLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        int left = lay.Room[At(4, 2)], right = lay.Room[At(13, 2)], door = lay.DoorAt[At(11, 2)];

        // 왼쪽 방 급전선(배전반 p → 통풍구) 길의 둘째 칸을 적신다 — 연장선이 지날 길(문 → 통풍구)과 겹치지 않는 곳
        (Sim s, Network net, ShipMind sm, Mind m, int wetCell) Flooded(ulong seed)
        {
            var s = Worlds.Full(lay, seed);
            var net = s.Get<Network>(); var m = s.Get<Mind>(); var f = s.Get<Field>();
            m.AddRobot(s, At(12, 3), AgentKind.Robot, At(12, 3));
            s.Run(3);
            var path = net.Feeders[net.RoomFeeder[left]].Path;
            var jp = net.JumperPath(right, left)!;
            int wet = path.Skip(1).First(c => Array.IndexOf(jp, c) < 0);
            f.AddStore(wet, f.T.sWater, 60);
            return (s, net, s.Get<ShipMind>(), m, wet);
        }

        // ── 연장선: 젖은 급전선은 다시 올릴 수 없다 → 2분 넘게 어두운 생명유지실 → 로봇이 오른쪽 방에서 연장선을 끌어와 산소 발생기가 다시 돈다 · 문틀의 문은 닫히지 않는다 ──
        {
            var (s, net, sm, m, wet) = Flooded(1);
            int o2 = net.Devices.First(d => d.Type == "OxygenGenerator").Id;
            bool darkAt3 = false;
            for (int k = 0; k < 40 && net.Jumpers.Count == 0; k++) { s.Run(Time.Minutes(0.5)); if (k == 6) darkAt3 = !net.RoomPowered(left); }
            s.Run(10);
            var st = s.Get<Structure>();
            string story = s.Facts.Since(0).Where(x => x.Kind == FactKind.Jumper && x.Other == 1).Select(x => s.Causes.Story(x.Cause)).FirstOrDefault() ?? "";
            if (Debug) Console.WriteLine("    " + string.Join("\n    ", sm.Log.TakeLast(10).Select(x => $"{Time.Clock(x.tick)} {x.text}")));
            Check("연장선: 젖은 급전선은 다시 올릴 수 없어 생명유지실이 어둡다 → 로봇이 오른쪽 방에서 연장선을 끌어와 산소 발생기가 다시 돈다 · 깔린 문틀의 문은 닫히지 않는다 (원인: 젖었다 → 합선 → 정전 → 연장선)",
                darkAt3 && net.Jumpers.Count == 1 && net.Working(o2) && st.DoorTarget[door] >= 0.99f && story.Contains("젖었다") && story.Contains("연장선"),
                $"3분째 어둠 {darkAt3} · 연장선 {net.Jumpers.Count} · 산소 발생기 {net.Working(o2)} · 문 목표 {st.DoorTarget[door]:0.0} · \"{story}\"");

            // ── 연장선이 깔린 문 칸이 젖으면 합선 — 옆 방 급전선까지 끊긴다 ──
            var f = s.Get<Field>();
            f.AddStore(At(11, 2), f.T.sWater, 80);
            s.Run(5);
            string sc = s.Facts.Since(0).Where(x => x.Kind == FactKind.Short).Select(x => s.Causes.Story(x.Cause)).LastOrDefault() ?? "";
            Check("연장선이 깔린 문 칸이 젖으면 합선 — 연장선이 타고 옆 방 급전선까지 끊긴다",
                net.Jumpers.Count == 0 && net.JumperShorts == 1 && net.Breaker[net.RoomFeeder[right]] == 0 && sc.Contains("연장선"),
                $"연장선 {net.Jumpers.Count} · 연장선 합선 {net.JumperShorts} · 오른쪽 차단기 {net.Breaker[net.RoomFeeder[right]]} · \"{sc}\"");
        }

        // ── 봉쇄가 먼저: 연장선을 받은 방에 불이 나면 주컴퓨터가 문틀의 연장선을 뽑고 문을 닫는다 (그 방은 다시 어둡다) ──
        {
            var (s, net, sm, m, wet) = Flooded(2);
            for (int k = 0; k < 40 && net.Jumpers.Count == 0; k++) s.Run(Time.Minutes(0.5));
            bool laid = net.Jumpers.Count == 1;
            s.Get<Chemistry>().Ignite(At(8, 3), 3e6, s.Causes.Add(CauseKind.Setup, "생명유지실 의자에 불씨", s.Tick));
            s.Run(Time.Minutes(3));
            var f = s.Get<Field>();
            bool cut = sm.JumpersCut >= 1 && net.Jumpers.Count == 0, darkAgain = !net.RoomPowered(left);
            int closeMin = -1;
            for (int k = 0; k < 20 && closeMin < 0; k++) { if (f.DoorOpen[door] < 0.1f) closeMin = 3 + k; else s.Run(Time.Minutes(1)); }   // 곁에 로봇이 지나다니면 문은 비킨 뒤 닫힌다
            if (Debug) Console.WriteLine("    " + string.Join("\n    ", sm.Log.TakeLast(14).Select(x => $"{Time.Clock(x.tick)} {x.text}")) + $"\n    문 목표 {s.Get<Structure>().DoorTarget[door]} · 로봇 칸 ({lay.X(m.Agents[0].C.Cell) - 1},{lay.Y(m.Agents[0].C.Cell) - 1}) · 기다리는 문 {m.Agents[0].C.WaitDoor} · 목표 {m.Agents[0].C.Goal} · 전지 {m.Agents[0].C.Battery:0.00} · 단계 {(m.Agents[0].C.StepIndex < m.Agents[0].Plan.Count ? m.Agents[0].Plan[m.Agents[0].C.StepIndex].Kind : StepKind.None)} · 망가짐 {m.Agents[0].C.Damage:0.00} · 문 {f.DoorOpen[door]:0.00}");
            Check("봉쇄가 먼저: 연장선을 받은 생명유지실에 불이 나면 주컴퓨터가 문틀의 연장선을 뽑고 문을 닫는다 (그 방은 다시 어둡다)",
                laid && cut && darkAgain && closeMin >= 0,
                $"깔림 {laid} · 뽑음 {sm.JumpersCut} · 다시 어둠 {darkAgain} · 문이 닫힌 때 불 뒤 {closeMin}분 · \"{sm.Log.LastOrDefault(x => x.text.Contains("연장선")).text}\"");
        }

        // ── 떼어 쓰기: 부품 주머니가 빈 로봇 · 작업대에 전기 부품도 없다 → 커피 기계에서 회로를 떼어 산소 발생기를 고친다 (헌 부품 — 닳은 만큼 그대로 · 커피 기계는 멈춘다) ──
        {
            var s = Worlds.Full(lay, 3);
            var net = s.Get<Network>(); var m = s.Get<Mind>();
            int robot = m.AddRobot(s, At(12, 3), AgentKind.Robot, At(12, 3));
            m.Agents[robot].C.Spares = 0;
            foreach (var fu in lay.Furniture) for (int i = 0; i < m.ItemCount; i++) m.Stock[fu.Id * m.ItemCount + i] = fu.Type == "Workbench" && m.ItemNames[i] is "수리 꾸러미" or "전기 부품" ? 0 : m.Stock[fu.Id * m.ItemCount + i];   // ⑪ 회로는 전기 부품으로 고친다
            int o2 = net.Devices.First(d => d.Type == "OxygenGenerator").Id, coffee = net.Devices.First(d => d.Type == "CoffeeMachine").Id;
            net.Age(coffee, Part.Electric, 0.6);
            float donorWear = net.WearFraction(coffee, Part.Electric);
            net.Broken[o2 * 4 + (int)Part.Electric] = 1;
            for (int k = 0; k < 12 && net.Broken[o2 * 4] != 0; k++) s.Run(Time.Minutes(5));
            string story = s.Facts.Since(0).Where(x => x.Kind == FactKind.Repaired && x.Other == o2).Select(x => s.Causes.Story(x.Cause)).FirstOrDefault() ?? "";
            Check("떼어 쓰기: 부품 주머니가 비고 전기 부품도 없으면 커피 기계에서 회로를 떼어 산소 발생기를 고친다 — 헌 부품이라 수명을 쓴 몫이 그대로 · 커피 기계는 멈춘다 · 장부가 맞는다",
                net.Broken[o2 * 4] == 0 && net.Broken[coffee * 4] != 0 && Math.Abs(net.WearFraction(o2, Part.Electric) - donorWear) < 0.01f && story.Contains("떼어") && m.Salvages == 1
                && Math.Abs(s.Book.OfItem(m.ItemIndex("떼어 낸 부품"), Flow.Salvaged) + s.Book.OfItem(m.ItemIndex("떼어 낸 부품"), Flow.Used)) < 1e-9,
                $"산소 발생기 고장 {net.Broken[o2 * 4]} · 커피 기계 고장 {net.Broken[coffee * 4]} · 수명 쓴 몫 {net.WearFraction(o2, Part.Electric):0.00}/{donorWear:0.00} · \"{story}\"");
        }

        // ── 물 재생기 봉함: 돌 때는 분에 0.5L(압력) · 멈추면 스미는 만큼 — 바닥 물이 그 방 급전선 길을 적시면 합선 (원인: 봉함 → 젖었다 → 합선) ──
        {
            (double water, double leaked, long shorts, string story) Run(bool running)
            {
                var s = Worlds.Full(lay, 4);
                var net = s.Get<Network>(); var f = s.Get<Field>();
                int wr = net.Devices.First(d => d.Type == "WaterRecycler").Id;
                if (!running) net.SetOn(wr, false);
                int c0 = s.Causes.Add(CauseKind.Setup, "물 재생기 봉함이 삭았다", s.Tick);
                net.AgeLeft(wr, Part.Seal, 0.01, c0);
                s.Run(Time.Minutes(10));
                double water = lay.Rooms[left].Cells.Sum(c => f.Stored(f.T.sWater, c));
                if (Debug && running) Console.WriteLine($"    길 {string.Join(",", net.Feeders[net.RoomFeeder[left]].Path.Select(c => $"({(lay.X(c) - 1)},{(lay.Y(c) - 1)})={f.Stored(f.T.sWater, c):0}"))} · 재생기 칸 ({(lay.X(net.Devices[wr].Cell) - 1)},{(lay.Y(net.Devices[wr].Cell) - 1)})={f.Stored(f.T.sWater, net.Devices[wr].Cell):0}");
                string story = s.Facts.Since(0).Where(x => x.Kind == FactKind.Short).Select(x => s.Causes.Story(x.Cause)).FirstOrDefault() ?? "";
                return (water, -s.Book.Of(Res.Water, Flow.Leaked), net.Shorts, story);
            }
            var on = Run(true); var off = Run(false);
            Check("물 재생기 봉함이 망가지면: 돌 때는 분에 0.5L 새어 바닥에 고이고 (멈췄으면 스미는 만큼) · 젖은 바닥이 그 방 급전선 길이면 합선 (봉함 → 오수 관이 샌다 → 젖었다 → 합선) — 합선으로 펌프가 서면 스미는 만큼으로 준다",
                on.leaked > 100 && on.leaked < 0.46 * 600 && off.leaked < on.leaked / 5 && on.water > 80 && on.shorts >= 1 && on.story.Contains("봉함") && on.story.Contains("젖었다"),
                $"10분 샌 물: 돌 때 {on.leaked:0}몰 ({on.leaked * 0.018:0.0}L · 내내 돌았으면 {0.46 * 600 * 0.018:0.0}L) · 멈춤 {off.leaked:0}몰 · 바닥 {on.water:0}몰 · 합선 {on.shorts} · \"{on.story}\"");
        }
    }
}
