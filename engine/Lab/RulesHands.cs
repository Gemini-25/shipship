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
            int floor = m.Loose.Count(x => x.Item == m.iExt || x.Item == m.iCO2);
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
                m.Patched > patched0 && f.Hole[hull] <= 0, $"막음 {m.Patched - patched0} · 구멍 {f.Hole[hull] * 1e4:0.0}cm² · 바닥 소화기 {m.Loose.Count(x => x.Item == m.iExt)}");
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
            for (long t = 0; t < Time.Hours(3); t++) { s.Step(); if (m.Loose.Any(x => x.Item == m.iSuit)) laid = true; }
            int suits1 = lockers.Sum(fu => m.Stock[fu * m.ItemCount + m.iSuit]);
            Check("덤벙대는 이의 우주복: 그 자리에 내려놓은 우주복을 주컴퓨터가 카메라로 보고 · 로봇이 없으면 일하는 사람에게 줍기를 맡겨 제자리로",
                laid && suits1 == suits0 + 1 && !m.Loose.Any(x => x.Item == m.iSuit), $"내려놓음 {laid} · 보관함 {suits0} → {suits1} · 주움 {m.Gathered} · 바닥 {m.Loose.Count(x => x.Item == m.iSuit)} · 든 이 {string.Join(",", m.Agents.Where(a => a.C.Holding == m.iSuit).Select(a => $"{m.NameOf(a.Id)}({a.C.Consc:0.0} {a.C.Goal} {m.Thought(a.Id)})"))}");
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
            m.Loose.Add(new LooseItem { Item = m.iExt, Cell = c1, Charge = 500 });
            m.Loose.Add(new LooseItem { Item = m.iMedkit, Cell = c2, Charge = 0 });
            s.Run(Time.Hours(1));
            bool extBack = !m.Loose.Any(x => x.Item == m.iExt) && ExtInBoxes(s) == box0 + 1 && ExtHeld(m) == 0;
            bool kitBack = !m.Loose.Any(x => x.Item == m.iMedkit) && m.Agents.All(a => a.C.Holding != m.iMedkit);
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
                int n0 = m.Loose.Count(x => x.Item == m.iExt);
                byte h0 = ps[0].C.Holding, h1 = ps[1].C.Holding;
                s.Step();
                if (m.Loose.Count(x => x.Item == m.iExt) > n0) { if (h0 == m.iExt && ps[0].C.Holding == 255) carefulLaid = true; if (h1 == m.iExt && ps[1].C.Holding == 255) carelessLaid = true; }
            }
            Check("꼼꼼한 이와 덤벙대는 이: 소화기를 든 채 배가 고프면 꼼꼼한 이는 제자리에 넣고 먹으러 가고 · 덤벙대는 이는 그 자리에 내려놓는다",
                !carefulLaid && carelessLaid && ps[0].C.Holding == 255 && ExtInBoxes(s) >= box0 + 1, $"꼼꼼 내려놓음 {carefulLaid} · 덤벙 내려놓음 {carelessLaid} · 보관함 {box0} → {ExtInBoxes(s)}");
        }
    }
}
