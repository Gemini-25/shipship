using ShipEngine;

namespace Lab;

/// <summary>손 (판단 결함 넷 — docs/REVIEW.md ③-3 · 함장 결정 16 (가) · docs/MIND.md 3단): 손 비우기 · 돌려놓기 · 쓴 소화기 채우기 · 바닥 물건 줍기 · 우주복을 걸어가 넣기.</summary>
public static partial class Program
{
    private static int ExtInBoxes(Sim s)
    {
        var m = s.Get<Mind>();
        int n = 0;
        for (int fu = 0; fu < s.Layout.Furniture.Count; fu++) n += m.Stock[fu * m.ItemCount + m.iExt] + m.Stock[fu * m.ItemCount + m.iCO2];
        return n;
    }

    private static int ExtHeld(Mind m) => m.Agents.Count(a => a.C.Holding == m.iExt || a.C.Holding == m.iCO2);

    private static void HandsRules()
    {
        if (!Do("손")) return;
        Console.WriteLine("\n[손 — 비우기 · 돌려놓기 · 채우기 · 줍기]");
        // ── 불 뒤 돌려놓기: 화살호 침대 불 — 2시간 뒤 손에 든 소화기 0 · 보관함 소화기가 처음만큼 (예전 로봇 셋 · 사람 둘이 든 채 · 보관함 11 → 5) ──
        {
            var s = ShipWorld.Crewed("Hwasal", 11, 3, 3, 0);
            var m = s.Get<Mind>(); var ch = s.Get<Chemistry>();
            s.Run(Time.Minutes(10));
            int cell = s.Layout.Furniture.First(f => f.Type == "Bed").Cells[0];
            int box0 = ExtInBoxes(s);
            ch.Ignite(cell, 3e6, 0);
            long ext0 = m.Extinguished;
            s.Run(Time.Hours(2));
            int box1 = ExtInBoxes(s), held = ExtHeld(m);
            int floor = Worlds.Items(s).Count(x => x.Item == m.iExt || x.Item == m.iCO2);
            Check("불 뒤 돌려놓기: 불을 끈 뒤 2시간 — 손에 든 소화기 0 · 보관함 소화기가 처음만큼 (예전엔 로봇 · 사람이 든 채로 남아 11 → 5)",
                m.Extinguished > ext0 && held == 0 && floor == 0 && box1 == box0, $"끔 {m.Extinguished - ext0} · 보관함 {box0} → {box1} · 손 {held} · 바닥 {floor}");
        }
        // ── 든 채 맡긴 일: 화살호 로봇들이 반쯤 쓴 소화기를 든 채 — 식량 나르기를 맡으면 먼저 내려놓고 나른다 (예전 699분 동안 안 해 배식기 0) ──
        {
            var s = ShipWorld.Crewed("Hwasal", 11, 3, 3, 0);
            var m = s.Get<Mind>(); var w = s.Get<Work>(); var f = s.Get<Field>();
            s.Run(Time.Minutes(10));
            foreach (var a in m.Agents) if (a.C.Kind == AgentKind.Robot) { a.C.Holding = m.iExt; a.C.HoldCharge = 30; }
            foreach (int p in w.FoodPoints) foreach (int c in s.Layout.Furniture[p].Cells) f.TakeStore(c, s.Tables.sFood, 1e9);
            double food0 = w.FoodPoints.Sum(p => w.FoodAt(p));
            s.Run(Time.Minutes(90));
            double food1 = w.FoodPoints.Sum(p => w.FoodAt(p));
            Check("든 채 맡긴 일: 소화기를 든 로봇이 식량 나르기를 맡으면 먼저 제자리에 내려놓고 나른다 (예전엔 손이 차 699분 동안 안 했다)",
                food1 > food0 + 0.5 && m.Agents.Where(a => a.C.Kind == AgentKind.Robot).All(a => a.C.Holding != m.iExt), $"배식기 · 냉장고 식량 {food0:0.0} → {food1:0.0}몰 · {string.Join(" ", m.Agents.Where(a => a.C.Kind == AgentKind.Robot).Select(a => a.C.Holding == 255 ? "-" : m.ItemNames[a.C.Holding]))}");
        }
        // ── 든 채 급한 일: 로봇 없는 화살호 — 소화기를 든 사람 곁 외판에 구멍 → 그 자리에 내려놓고 봉합 꾸러미로 막는다 (예전 30분 동안 막지 못함) ──
        {
            var s = ShipWorld.Crewed("Hwasal", 11, 3, 0, 0);
            var m = s.Get<Mind>(); var st = s.Get<Structure>(); var f = s.Get<Field>(); var lay = s.Layout;
            s.Run(Time.Minutes(10));
            var p = m.Agents.First(a => a.IsPerson && a.C.Up != 0);
            foreach (var a in m.Agents) if (a.IsPerson) { a.C.Holding = m.iExt; a.C.HoldCharge = 400; }
            int room = lay.Room[p.C.Cell];
            int hull = Enumerable.Range(0, lay.N).First(c => lay.Hull[c] && new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Any(d =>
            {
                int x = lay.X(c) + d.Item1, y = lay.Y(c) + d.Item2;
                return x >= 0 && y >= 0 && x < lay.W && y < lay.H && lay.Room[lay.Index(x, y)] == room;
            }));
            st.Impact(s, hull, 20_000, 1, 0, s.Causes.Add(CauseKind.Impact, "파편이 외판을 쳤다", s.Tick, hull));
            long patched0 = m.Patched;
            s.Run(Time.Minutes(30));
            Check("든 채 급한 일: 소화기를 든 사람이 곁 외판의 구멍을 보면 그 자리에 내려놓고 봉합 꾸러미를 가져와 막는다 (예전엔 손이 차 30분 동안 막지 못했다)",
                m.Patched > patched0 && f.Hole[hull] <= 0, $"막음 {m.Patched - patched0} · 구멍 {f.Hole[hull] * 1e4:0.0}cm² · 바닥 소화기 {Worlds.Items(s).Count(x => x.Item == m.iExt)}");
        }
        // ── 우주복은 걸어가 넣는다: 숨쉴 만한 방에서 벗으면 손에 들고 보관함으로 (남은 산소 그대로) — 예전엔 벗는 순간 보관함으로 옮아 갔고 꺼내면 가득 찼다 ──
        {
            var s = ShipWorld.Crewed("Hwasal", 11, 3, 0, 0);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var lay = s.Layout;
            s.Run(Time.Minutes(10));
            var p = m.Agents.First(a => a.IsPerson && a.C.Up != 0);
            var lockers = lay.Furniture.Where(fu => fu.Type == "SuitLocker").Select(fu => fu.Id).ToArray();
            int suits0 = lockers.Sum(fu => m.Stock[fu * m.ItemCount + m.iSuit]);
            life[p.C.Body].Suit = 1; life[p.C.Body].SuitO2 = 33; p.C.Consc = 0.8f;
            p.C.Goal = GoalKind.Idle; p.Plan.Clear();
            s.Step(); s.Step(); s.Step();
            bool holding = p.C.Holding == m.iSuit && Math.Abs(p.C.HoldCharge - 33) < 1 && lockers.Sum(fu => m.Stock[fu * m.ItemCount + m.iSuit]) == suits0;
            s.Run(Time.Minutes(30));
            int suits1 = lockers.Sum(fu => m.Stock[fu * m.ItemCount + m.iSuit]);
            var spent = m.Spent.Where(x => x.Item == m.iSuit).ToList();
            Check("우주복은 걸어가 넣는다: 벗으면 손에 들고 (보관함은 그대로) · 걸어가 넣는다 · 남은 산소 그대로 적힌다 (예전엔 벗는 순간 보관함에 · 꺼내면 가득)",
                holding && suits1 == suits0 + 1 && p.C.Holding != m.iSuit && (spent.Any(x => Math.Abs(x.Charge - 33) < 1.5) || m.Refilled > 0),
                $"벗은 뒤 손 {holding} · 보관함 {suits0} → {suits1} · 남은 양 [{string.Join(" ", spent.Select(x => x.Charge.ToString("0")))}] · 채움 {m.Refilled}");
        }
        // ── 덤벙대는 이의 우주복: 벗어 그 자리에 내려놓으면 (로봇이 없는 배) 주컴퓨터가 카메라로 보고 일하는 사람에게 줍기를 맡긴다 ──
        {
            var s = ShipWorld.Crewed("Hwasal", 11, 3, 0, 0);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var lay = s.Layout;
            s.Run(Time.Minutes(10));
            var p = m.Agents.First(a => a.IsPerson && a.C.Up != 0);
            var lockers = lay.Furniture.Where(fu => fu.Type == "SuitLocker").Select(fu => fu.Id).ToArray();
            int suits0 = lockers.Sum(fu => m.Stock[fu * m.ItemCount + m.iSuit]);
            life[p.C.Body].Suit = 1; life[p.C.Body].SuitO2 = 40; p.C.Consc = 0.1f;
            p.C.Goal = GoalKind.Idle; p.Plan.Clear();
            bool laid = false;
            for (long t = 0; t < 30; t++) { s.Step(); if (Worlds.Items(s).Any(x => x.Item == m.iSuit)) laid = true; }   // 내려놓고(첫 틱) 주워 제자리에 넣은 때(15틱)의 두 배 (예전 3시간)
            int suits1 = lockers.Sum(fu => m.Stock[fu * m.ItemCount + m.iSuit]);
            Check("덤벙대는 이의 우주복: 그 자리에 내려놓은 우주복을 주컴퓨터가 카메라로 보고 · 로봇이 없으면 일하는 사람에게 줍기를 맡겨 제자리로",
                laid && suits1 == suits0 + 1 && !Worlds.Items(s).Any(x => x.Item == m.iSuit), $"내려놓음 {laid} · 보관함 {suits0} → {suits1} · 주움 {m.Gathered} · 바닥 {Worlds.Items(s).Count(x => x.Item == m.iSuit)} · 든 이 {string.Join(",", m.Agents.Where(a => a.C.Holding == m.iSuit).Select(a => $"{m.NameOf(a.Id)}({a.C.Consc:0.0} {a.C.Goal} {m.Thought(a.Id)})"))}");
        }
        // ── 쓴 소화기 채우기: 반쯤 쓴 소화기 → 보관함 (남은 양) → 주컴퓨터가 채우기를 맡긴다 → 로봇이 물꼭지에서 물탱크 물로 채워 되돌린다 (장부 '소화기에 담았다') ──
        {
            var s = ShipWorld.Crewed("Hwasal", 11, 3, 3, 0);
            var m = s.Get<Mind>();
            s.Run(Time.Minutes(10));
            var r = m.Agents.First(a => a.C.Kind == AgentKind.Robot);
            r.C.Holding = m.iExt; r.C.HoldCharge = 100;
            double book0 = s.Book.Of(Res.Water, Flow.Refill);
            bool spentSeen = false;
            for (long t = 0; t < Time.Hours(2); t++) { s.Step(); if (m.Spent.Any(x => x.Item == m.iExt)) spentSeen = true; }
            double book = s.Book.Of(Res.Water, Flow.Refill) - book0;
            Check("쓴 소화기 채우기: 반쯤 쓴 소화기는 남은 양 그대로 보관함에 → 로봇이 물꼭지에서 물탱크 물로 채워 되돌린다 (장부 '소화기에 담았다' 400몰)",
                spentSeen && m.Refilled > 0 && !m.Spent.Any(x => x.Item == m.iExt) && Math.Abs(book + 400) < 1, $"보관함에 쓴 것 {spentSeen} · 채움 {m.Refilled} · 장부 {book:0.0}몰 · 남은 쓴 것 {m.Spent.Count(x => x.Item == m.iExt)}");
        }
        // ── 바닥 물건 줍기: 통로에 떨어진 소화기(큰 물건 — 카메라가 본다)와 로봇 곁의 구급 상자(작은 물건 — 로봇이 보고 줍는다) ──
        {
            var s = ShipWorld.Crewed("Hwasal", 11, 3, 3, 0);
            var m = s.Get<Mind>(); var lay = s.Layout;
            s.Run(Time.Minutes(10));
            var corr = lay.Rooms.First(r => r.Type == "Corridor");
            int c1 = corr.Cells[corr.Cells.Length / 2];
            var r0 = m.Agents.First(a => a.C.Kind == AgentKind.Robot);
            int c2 = lay.Rooms[lay.Room[r0.C.Cell]].Cells.First(c => c != r0.C.Cell && lay.Kind[c] == CellKind.Floor && lay.FurnitureAt[c] < 0);
            int box0 = ExtInBoxes(s);
            Worlds.PutItem(s, m.iExt, c1, 500);
            Worlds.PutItem(s, m.iMedkit, c2, 0);
            s.Run(Time.Hours(1));
            bool extBack = !Worlds.Items(s).Any(x => x.Item == m.iExt) && ExtInBoxes(s) == box0 + 1 && ExtHeld(m) == 0;
            bool kitBack = !Worlds.Items(s).Any(x => x.Item == m.iMedkit) && m.Agents.All(a => a.C.Holding != m.iMedkit);
            Check("바닥 물건 줍기: 통로의 소화기는 주컴퓨터가 보고 줍기를 맡기고 · 로봇 곁의 구급 상자는 로봇이 보고 줍는다 — 둘 다 제자리에 (예전엔 아무도 줍지 않았다)",
                extBack && kitBack, $"소화기 {extBack} · 구급 상자 {kitBack} · 주움 {m.Gathered}");
        }
        // ── 꼼꼼한 이와 덤벙대는 이: 소화기를 든 채 배가 고프면 — 꼼꼼한 이는 제자리에 넣고 먹으러 가고, 덤벙대는 이는 그 자리에 내려놓는다 (로봇이 줍는다) ──
        {
            var s = ShipWorld.Crewed("Hwasal", 11, 3, 0, 0);
            var m = s.Get<Mind>();
            s.Run(Time.Minutes(10));
            var ps = m.Agents.Where(a => a.IsPerson && a.C.Up != 0).Take(2).ToArray();
            ps[0].C.Consc = 0.9f; ps[1].C.Consc = 0.1f;
            foreach (var p in ps) { p.C.Holding = m.iExt; p.C.HoldCharge = 500; p.C.Hunger = 0.9f; p.C.Sleepy = 0; p.Plan.Clear(); p.C.Goal = GoalKind.Idle; }
            int box0 = ExtInBoxes(s);
            bool carefulLaid = false, carelessLaid = false;
            for (long t = 0; t < Time.Minutes(30); t++)
            {
                int n0 = Worlds.Items(s).Count(x => x.Item == m.iExt);
                byte h0 = ps[0].C.Holding, h1 = ps[1].C.Holding;
                s.Step();
                if (Worlds.Items(s).Count(x => x.Item == m.iExt) > n0) { if (h0 == m.iExt && ps[0].C.Holding == 255) carefulLaid = true; if (h1 == m.iExt && ps[1].C.Holding == 255) carelessLaid = true; }
            }
            Check("꼼꼼한 이와 덤벙대는 이: 소화기를 든 채 배가 고프면 꼼꼼한 이는 제자리에 넣고 먹으러 가고 · 덤벙대는 이는 그 자리에 내려놓는다",
                !carefulLaid && carelessLaid && ps[0].C.Holding == 255 && ExtInBoxes(s) >= box0 + 1, $"꼼꼼 내려놓음 {carefulLaid} · 덤벙 내려놓음 {carelessLaid} · 보관함 {box0} → {ExtInBoxes(s)}");
        }
        // ── 줍기는 고른 그 물건 (PH3b): 로봇 방 한 칸에 둘 곳 없는 물건이 먼저 · 소화기가 뒤에 — 로봇은 소화기를 줍는다 (예전엔 그 칸의 첫 물건을 집으려다 둘 곳이 없어 계획이 깨졌다) ──
        // 줍기는 곁 칸에서: 줍는 이는 물체 칸에 들어서지 않는다 (물체가 움직이면 밟히지 않게 — 곁에서 손을 뻗는다)
        {
            var s = ShipWorld.Crewed("Hwasal", 11, 0, 1, 0);
            var m = s.Get<Mind>(); var lay = s.Layout;
            s.Run(Time.Minutes(10));
            var r0 = m.Agents.First(a => a.C.Kind == AgentKind.Robot);
            var room = lay.Rooms[lay.Room[r0.C.Cell]];
            int cell = room.Cells.Where(c => lay.Kind[c] == CellKind.Floor && lay.FurnitureAt[c] < 0 && m.Dist(c, r0.C.Cell) >= 3).OrderBy(c => c).First();
            byte stray = (byte)Enumerable.Range(0, m.ItemCount).First(i => i != m.iWaste && i != m.iScrap && i != m.iScrapElec && !m.IsFood(i) && m.HomeFor((byte)i, cell) < 0);   // 둘 곳이 없는 물건 (표에서)
            int box0 = ExtInBoxes(s);
            Worlds.PutItem(s, stray, cell, 0);
            Worlds.PutItem(s, m.iExt, cell, 500);
            bool stepped = false;
            for (long t = 0; t < Time.Hours(1) && Worlds.Items(s).Any(x => x.Item == m.iExt); t++)
            {
                s.Step();
                if (Worlds.Items(s).Any(x => x.Item == m.iExt) && m.Agents.Any(a => a.C.Goal == GoalKind.Gather && a.C.Cell == cell)) stepped = true;
            }
            s.Run(Time.Minutes(20));
            bool extBack = !Worlds.Items(s).Any(x => x.Item == m.iExt) && ExtInBoxes(s) == box0 + 1 && ExtHeld(m) == 0;
            bool strayLeft = Worlds.Items(s).Count(x => x.Item == stray) == 1;
            Check("줍기는 고른 그 물건: 한 칸에 둘 곳 없는 물건과 소화기 — 로봇이 소화기를 주워 제자리에 (둘 곳 없는 것은 그대로) (예전엔 그 칸의 첫 물건을 집으려다 계획이 깨졌다)",
                extBack && strayLeft, $"소화기 {extBack} · 둘 곳 없는 {m.ItemNames[stray]} 남음 {strayLeft} · 주움 {m.Gathered} · 로봇 {r0.C.Goal} {m.Thought(r0.Id)}");
            Check("줍기는 곁 칸에서: 줍는 로봇은 물체 칸에 들어서지 않고 곁 칸에서 줍는다 (예전엔 그 칸에 올라섰다)",
                extBack && !stepped, $"주움 {extBack} · 올라섬 {stepped}");
        }
        // ── 오물 통 알림은 그 통 (PH3b): 통을 든 로봇 둘이 한 칸에서 멈춘다 → 내려놓인 통 둘을 다른 로봇이 하나씩 주워 쏟는다 (예전엔 알림이 칸이라 하나를 주우면 다른 통이 잊혔다) ──
        {
            var legend = new Dictionary<char, string> { ['e'] = "Bed", ['r'] = "RobotDock", ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer",
                ['T'] = "Toilet", ['S'] = "Shower", ['k'] = "Sink", ['o'] = "Composter" };
            var lay = Worlds.Ascii(WashCareShip, legend);
            var s = Worlds.Full(lay, 17);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>();
            int here = Worlds.At(lay, 30, 1), dock = Worlds.At(lay, 6, 1);
            int r1 = m.AddRobot(s, here, AgentKind.Robot, dock), r2 = m.AddRobot(s, here, AgentKind.Robot, dock), r3 = m.AddRobot(s, Worlds.At(lay, 5, 1), AgentKind.Robot, dock);
            s.Run(30);
            var toilets = lay.Furniture.Where(f => f.Type == "Toilet").Select(f => f.Id).ToArray();
            int bin = lay.Furniture.First(x => x.Type == "Composter").Id;
            double kg = 0;
            foreach (var (r, k) in new[] { (r1, 0), (r2, 1) })
            {
                life.Tank[toilets[k]] = 3 + k;
                m[r].Plan.Clear(); m[r].C.Goal = GoalKind.Idle;
                m[r].C.Holding = m.iWaste; m[r].C.HoldCharge = (float)life.LiftTank(toilets[k]); kg += m[r].C.HoldCharge;
                s.Book.AddItem(m.iWaste, Flow.Removed, 1);
                net.Robot(m[r].C.Mech).Battery = 0;   // 통을 든 채 한 칸에서 멈춘다
            }
            s.Step();
            int down = Worlds.Items(s).Count(x => x.Item == m.iWaste), told = m.WasteDown.Count;
            for (long k = 0; k < Time.Hours(2) && m.Emptied < 2; k++) s.Step();
            Check("오물 통 알림은 그 통: 한 칸에 내려놓인 통 둘을 저마다 알린다 → 다른 로봇이 둘 다 주워 퇴비통에 쏟는다 — 질량 그대로 (예전엔 칸으로 알려 하나를 주우면 다른 통이 잊혔다)",
                down == 2 && told == 2 && m.Emptied == 2 && !Worlds.Items(s).Any(x => x.Item == m.iWaste) && m.WasteDown.Count == 0 && Math.Abs(life.Heap[bin] - kg) < 1e-3,
                $"내려놓인 통 {down} · 알림 {told} · 비움 {m.Emptied} · 바닥 통 {Worlds.Items(s).Count(x => x.Item == m.iWaste)} · 남은 알림 {m.WasteDown.Count} · 퇴비통 {life.Heap[bin]:0.000}/{kg:0.000}kg · 셋째 로봇 {m[r3].C.Goal}");
        }
        // ── 저장 판 (PH3b 검토): 오물 통 알림 · 줍기 일감 · 맡김의 대상이 칸에서 물체 Id로 바뀌었다 — 그 뜻이 칸이던 판 106 저장(PH3a)은 거절하고 ·
        //    지금 판의 저장 왕복은 내려놓인 통을 그대로 알려 되살린 세계가 비운다 (예전엔 판이 106 그대로라 칸을 Id로 읽어 통이 영영 잊혔다) ──
        {
            var legend = new Dictionary<char, string> { ['e'] = "Bed", ['r'] = "RobotDock", ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer",
                ['T'] = "Toilet", ['S'] = "Shower", ['k'] = "Sink", ['o'] = "Composter" };
            var lay = Worlds.Ascii(WashCareShip, legend);
            var s = Worlds.Full(lay, 17);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>();
            int dock = Worlds.At(lay, 6, 1);
            int r1 = m.AddRobot(s, Worlds.At(lay, 30, 1), AgentKind.Robot, dock); m.AddRobot(s, Worlds.At(lay, 5, 1), AgentKind.Robot, dock);
            s.Run(30);
            int toilet = lay.Furniture.First(f => f.Type == "Toilet").Id;
            life.Tank[toilet] = 3;
            m[r1].Plan.Clear(); m[r1].C.Goal = GoalKind.Idle;
            m[r1].C.Holding = m.iWaste; m[r1].C.HoldCharge = (float)life.LiftTank(toilet);
            s.Book.AddItem(m.iWaste, Flow.Removed, 1);
            net.Robot(m[r1].C.Mech).Battery = 0;   // 통을 든 채 멈춘다 → 내려놓고 알린다
            s.Step();
            var save = s.Save();
            var old = (byte[])save.Clone();
            BitConverter.GetBytes(106).CopyTo(old, 8);   // 이름표(8바이트) 다음이 판 번호 — PH3a가 밖에 낸 판 (알림 · 일감 대상이 칸)
            string refused = "";
            try { new Sim(s.Setup).Load(old); } catch (InvalidDataException e) { refused = e.Message; }
            var back = new Sim(s.Setup); back.Load(save);
            var bm = back.Get<Mind>();
            int told = bm.WasteDown.Count;
            for (long k = 0; k < Time.Hours(2) && bm.Emptied < 1; k++) back.Step();
            Check("저장 판: 오물 통 알림 · 줍기 대상이 칸이던 판 106 저장은 거절한다 · 지금 판의 저장에서 되살린 세계는 내려놓인 통을 알린 그대로 주워 비운다 (예전엔 판이 같아 칸을 Id로 읽어 통이 영영 잊혔다)",
                refused.Contains("저장 판 106") && told == 1 && bm.Emptied == 1 && !Worlds.Items(back).Any(x => x.Item == bm.iWaste) && bm.WasteDown.Count == 0,
                $"판 106: \"{(refused.Length > 0 ? refused : "읽었다")}\" · 되살린 알림 {told} · 비움 {bm.Emptied} · 바닥 통 {Worlds.Items(back).Count(x => x.Item == bm.iWaste)} · 남은 알림 {bm.WasteDown.Count}");
        }
    }
}
