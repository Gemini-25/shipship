using ShipEngine;

namespace Lab;

/// <summary>⑪ 공급 사슬 규칙 시험 — 부품 갈래 · 원료 → 부품 · 재활용 · 무엇을 먼저 만들지 (docs/ENGINE.md ⑪ 공급 사슬).</summary>
public static partial class Program
{
    private static void SupplyChainRules()
    {
        if (!Do("공급 사슬")) return;
        Console.WriteLine("\n[⑪ 공급 사슬]");
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        string[] parts = { "전기 부품", "기계 부품", "봉함재", "거르개" };

        // 작업대 재고를 정한다 (갈래 부품 · 원료 · 전자 소자 · 수리 꾸러미는 모두 비우고 주어진 것만)
        void Bench(Mind m, Sim s, params (string item, int n)[] have)
        {
            int bench = s.Layout.Furniture.FindIndex(f => f.Type == "Workbench");
            foreach (string it in parts.Concat(new[] { "원료", "전자 소자", "수리 꾸러미", "봉합 꾸러미", "소화기", "이산화탄소 소화기" }))
                for (int fu = 0; fu < s.Layout.Furniture.Count; fu++) m.Stock[fu * m.ItemCount + m.ItemIndex(it)] = 0;
            foreach (var (it, n) in have) m.Stock[bench * m.ItemCount + m.ItemIndex(it)] = n;
        }

        // ── (가) 부품 갈래: 충전독의 전기 부품이 망가졌다 — 로봇은 전기 부품으로만 고친다 (기계 부품은 쓰지 않는다) · 주머니에 있으면 주머니 것 · 갈아 낸 부품은 독 곁에 ──
        {
            (bool fixedIt, int elec, int mech, int scrap, long salvaged, byte pouch) Run(int elecOnBench, bool pouch)
            {
                var s = Worlds.Full(lay, 7);
                var m = s.Get<Mind>(); var net = s.Get<Network>();
                int robot = m.AddRobot(s, At(8, 7), AgentKind.Robot, At(8, 7));
                if (!pouch) m.Agents[robot].C.Spares = 0;
                Bench(m, s, ("전기 부품", elecOnBench), ("기계 부품", 2));
                int dock = net.Devices.First(d => d.Type == "RobotDock" && s.Layout.Room[d.Cell] == s.Layout.Room[At(3, 5)]).Id;
                net.Age(dock, Part.Electric, 1.0, s.Causes.Add(CauseKind.Setup, "충전독 회로가 낡았다", s.Tick, net.Devices[dock].Cell));
                s.Run(Time.Hours(1));
                int fu = net.Devices[dock].Furniture;
                return (net.Broken[dock * 4 + (int)Part.Electric] == 0, m.TotalOf(m.ItemIndex("전기 부품")), m.TotalOf(m.ItemIndex("기계 부품")),
                    m.Stock[fu * m.ItemCount + m.iScrapElec], net.PartsSalvaged, m.Agents[robot].C.Spares);
            }
            var spare = Run(1, false); var none = Run(0, false); var bag = Run(1, true);
            Check("부품 갈래: 망가진 회로는 작업대의 전기 부품으로 고친다 · 갈아 낸 것은 '헌 전기 부품'으로 독 곁에 남는다",
                spare.fixedIt && spare.elec == 0 && spare.mech == 2 && spare.scrap == 1, $"고침 {spare.fixedIt} · 전기 부품 1 → {spare.elec} · 기계 부품 2 → {spare.mech} · 헌 전기 부품 {spare.scrap}");
            Check("전기 부품이 없으면 기계 부품은 쓰지 않는다 — 떼어 올 덜 중요한 장치(가전 · 등)도 없으면 고치지 못한 채 기다린다",
                none.mech == 2 && !none.fixedIt && none.salvaged == 0, $"기계 부품 2 → {none.mech} · 떼어 쓴 수 {none.salvaged} · 고침 {none.fixedIt}");
            Check("로봇 부품 주머니는 갈래마다 하나 — 회로를 고치면 주머니의 전기 부품만 줄고 작업대 것은 그대로",
                bag.fixedIt && bag.elec == 1 && bag.pouch == (Mind.RobotSpares & ~1), $"고침 {bag.fixedIt} · 작업대 전기 부품 {bag.elec} · 주머니 {Convert.ToString(bag.pouch, 2).PadLeft(4, '0')}");
        }

        // ── (나) 전자 소자는 배에서 만들 수 없다: 전기 부품이 모자라고 전자 소자가 없으면 막힌 곳이 그 까닭을 댄다 ──
        {
            var s = Worlds.Full(lay, 7);
            var m = s.Get<Mind>(); var sm = s.Get<ShipMind>();
            m.AddRobot(s, At(8, 7), AgentKind.Robot, At(8, 7));
            Bench(m, s, ("원료", 6), ("기계 부품", 2), ("봉함재", 2), ("거르개", 2), ("수리 꾸러미", 2), ("봉합 꾸러미", 4), ("소화기", 3), ("이산화탄소 소화기", 2));
            s.Run(ShipMind.ChoreEvery * 2);
            string line = sm.Blocks.FirstOrDefault(b => b.StartsWith("전기 부품")) ?? "";
            Check("전자 소자는 배에서 만들 수 없다: 전기 부품이 모자라면 막힌 곳 '전기 부품 ← 전자 소자가 없다 ← 배에서 만들 수 없다'",
                line.Contains("전자 소자가 없다 ← 배에서 만들 수 없다"), $"\"{line}\"");
        }

        // ── (라) 무엇을 먼저 만들지: 원료 둘 — 망가진 배식기가 기계 부품을 기다리면 그것부터 (봉합 꾸러미 · 봉함재 같은 채우기는 미룬다) ──
        {
            var s = Worlds.Full(lay, 7);
            var m = s.Get<Mind>(); var net = s.Get<Network>(); var sm = s.Get<ShipMind>();
            int robot = m.AddRobot(s, At(8, 7), AgentKind.Robot, At(8, 7));
            m.Agents[robot].C.Spares = 0;
            Bench(m, s, ("원료", 2), ("전기 부품", 2), ("거르개", 2), ("수리 꾸러미", 2), ("소화기", 3), ("이산화탄소 소화기", 2));
            int disp = net.Devices.First(d => d.Type == "MealDispenser").Id;
            net.Age(disp, Part.Mechanical, 1.0, s.Causes.Add(CauseKind.Setup, "배식기 모터가 닳았다", s.Tick, net.Devices[disp].Cell));
            s.Run(ShipMind.ChoreEvery + ShipMind.ChoreOffset + 1);
            var crafts = sm.Jobs.Where(j => j.Kind == GoalKind.Craft).Select(j => m.ItemNames[j.Arg]).ToList();
            string said = sm.Log.Select(l => l.text).FirstOrDefault(t => t.StartsWith("원료가 모자라")) ?? "";
            for (int k = 0; k < 12; k++)
            {
                s.Run(Time.Minutes(15));
                if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1")
                    Console.WriteLine($"    {Time.Clock(s.Tick)} 일 [{string.Join(", ", sm.Jobs.Select(j => $"{j.Kind}:{j.Agent}"))}] 로봇 {m.Agents[robot].C.Goal} 맡음 {m.Agents[robot].C.AssignedGoal} 듦 {m.Agents[robot].C.Holding} 전지 {m.Agents[robot].C.Battery:0.00} 막힘 {m.Agents[robot].C.Snag} · 기계 부품 {m.TotalOf(m.ItemIndex("기계 부품"))} 원료 {m.TotalOf(m.ItemIndex("원료"))}");
            }
            bool fixedIt = net.Broken[disp * 4 + (int)Part.Mechanical] == 0;
            Check("무엇을 먼저 만들지: 원료 둘 — 망가진 배식기가 기다리는 기계 부품부터 맡기고 채우기(봉합 꾸러미 · 봉함재)는 미룬다 · 방송 · 세 시간 안에 고친다",
                crafts.Count == 1 && crafts[0] == "기계 부품" && said.Contains("기계 부품을 먼저 만든다") && said.Contains("배식기") && fixedIt,
                $"맡긴 만들기 [{string.Join(", ", crafts)}] · \"{said}\" · 고침 {fixedIt}");
        }

        // ── (다) 재활용: 디딤호 — 장치 곁에 남은 헌 부품을 로봇이 정제기로 나르고 · 정제기가 30분에 하나씩 녹여 원료로 ──
        {
            var s = ShipWorld.Crewed("Didim", 7, 0, 2);
            var m = s.Get<Mind>(); var w = s.Get<Work>(); var sm = s.Get<ShipMind>();
            var ly = s.Layout;
            int refinery = ly.Furniture.FindIndex(f => f.Type == "Refinery");
            int far = ly.Furniture.Where(f => f.Type == "OxygenGenerator" || f.Type == "WaterRecycler").Select(f => f.Id).First();
            m.Stock[far * m.ItemCount + m.iScrap] = 2;
            m.Stock[refinery * m.ItemCount + m.ItemIndex("원료")] = 0;
            for (int k = 0; k < 12 && w.Recycled < 2; k++)
            {
                s.Run(Time.Minutes(15));
                if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1")
                    Console.WriteLine($"    {Time.Clock(s.Tick)} 일 [{string.Join(", ", sm.Jobs.Select(j => $"{j.Kind}:{j.Agent}"))}] 로봇 [{string.Join(", ", m.Agents.Where(a => !a.IsPerson).Select(a => $"{a.Id} {a.C.Goal} 맡음 {a.C.AssignedGoal} 듦 {a.C.Holding}"))}]");
            }
            Check("재활용: 장치 곁 헌 부품 둘을 나르기 일로 정제기까지 옮기고 · 정제기가 녹여 원료로 (30분에 하나)",
                m.Stock[far * m.ItemCount + m.iScrap] == 0 && w.Recycled >= 2 && m.Hauled >= 1,
                $"놓인 곳 헌 부품 2 → {m.Stock[far * m.ItemCount + m.iScrap]} · 나른 번 {m.Hauled} · 녹인 수 {w.Recycled} · 정제기 원료 {m.Stock[refinery * m.ItemCount + m.ItemIndex("원료")]}");
        }

        // ── (다) 헌 전기 부품: 녹이면 원료 하나 + 전자 소자를 건질 수도 (세상의 확률 — 1이면 넷 다 · 0이면 하나도) ──
        {
            (int chips, long recycled) Melt(double chance)
            {
                var s = ShipWorld.Create("Didim", 7);
                var m = s.Get<Mind>(); var w = s.Get<Work>();
                w.ChipBack = chance;
                int refinery = s.Layout.Furniture.FindIndex(f => f.Type == "Refinery");
                m.Stock[refinery * m.ItemCount + m.iScrapElec] = 4;
                m.Stock[refinery * m.ItemCount + m.ItemIndex("원료")] = 0;
                int before = m.TotalOf(m.iChip);
                for (int k = 0; k < 15 && w.Recycled < 4; k++) s.Run(Time.Minutes(10));
                return (m.TotalOf(m.iChip) - before, w.Recycled);
            }
            var all = Melt(1); var no = Melt(0);
            Check("헌 전기 부품을 녹이면 원료 + 전자 소자를 건질 수도 — 건질 확률 1: 넷 다 · 0: 하나도 (녹이는 것은 같다)",
                all.chips == 4 && no.chips == 0 && all.recycled == 4 && no.recycled == 4, $"확률 1: 전자 소자 +{all.chips} · 녹임 {all.recycled} / 확률 0: +{no.chips} · 녹임 {no.recycled}");
        }
        // ── (마) 보급: 교신한 상선에 모자란 것을 부탁 — 그 배의 형편(세상의 확률)으로 나눠 주면 맞춰 와서 붙고 · 받은 것은 작업대로 (장부 '보급') · 형편이 없으면 거절 ──
        {
            (long got, int chips, string log, double flow, bool undocked) Trade(double share)
            {
                var hl = Worlds.Ascii(HelmShip, HelmLegend);
                var s = Worlds.Full(hl, 21);
                var m = s.Get<Mind>(); var o = s.Get<Outside>(); var sm = s.Get<ShipMind>();
                o.ShareTrader = share;
                foreach (string it in parts.Concat(new[] { "원료", "전자 소자" })) for (int fu = 0; fu < hl.Furniture.Count; fu++) m.Stock[fu * m.ItemCount + m.ItemIndex(it)] = 0;
                s.Run(5);
                int before = m.TotalOf(m.iChip);
                int c0 = s.Causes.Add(CauseKind.Contact, "다른 배가 항로를 지난다", s.Tick);
                int id = o.AddContact(s, ContactKind.Trader, 1e7, 1, 0, 3000, 2e5, 0, 0, c0);
                for (int k = 0; k < 24 && sm.SuppliesIn == 0 && !sm.Log.Any(l => l.text.Contains("나눠 줄 것이 없다")); k++) s.Run(Time.Minutes(15));
                int ci = o.ContactIndex(id);
                return (sm.SuppliesIn, m.TotalOf(m.iChip) - before, string.Join(" / ", sm.Log.Select(l => l.text).Where(t => t.Contains("부탁") || t.Contains("나눠") || t.Contains("받았다"))),
                    s.Book.ItemFlow[(Book.Bulk + m.iChip - Book.Bulk) * Book.Flows + (int)Flow.Supplied], ci < 0 || o.Contacts[ci].State == ContactState.Gone);
            }
            var yes = Trade(1); var no = Trade(0);
            Check("보급: 교신한 상선에 모자란 것을 부탁하고 · 나눠 주면 그 배가 맞춰 와 붙어 넘겨준다 (작업대 · 장부 '보급') · 형편이 없으면 거절",
                yes.got > 0 && yes.chips > 0 && yes.flow > 0 && yes.undocked && yes.log.Contains("부탁") && yes.log.Contains("받았다") && no.got == 0 && no.log.Contains("나눠 줄 것이 없다"),
                $"받음 {yes.got} · 전자 소자 +{yes.chips} · 붙었다 놓음 {yes.undocked} · \"{yes.log}\" / 거절: 받음 {no.got} · \"{no.log}\"");
        }

        // ── (마) 정거장: 띠 보급소(1.21AU)는 황도면 가까운 항로(보통 길)만 곁을 지난다 — 높은 고요 길은 지나치지 못한다 ──
        {
            (bool met, int contacts) Pass(int route)
            {
                var s = Worlds.Full(lay, 23);
                var o = s.Get<Outside>();
                o.Route = route; o.TrafficOn = true; o.Distance = 1.21;
                s.Run(3);
                return ((o.StationsMet & 1) != 0, o.Contacts.Count(c => c.Kind == ContactKind.Station));
            }
            var low = Pass(4); var high = Pass(1);
            Check("정거장: 띠 보급소(1.21AU)는 황도면 가까운 항로(4 보통 길 · 0.6°)만 곁을 지나 교신 거리에 든다 · 높은 고요 길(2°)은 지나치지 못한다",
                low.met && low.contacts == 1 && !high.met && high.contacts == 0, $"보통 길: 지남 {low.met} · 정거장 {low.contacts} / 높은 고요 길: 지남 {high.met} · {high.contacts}");
        }
    }
}
