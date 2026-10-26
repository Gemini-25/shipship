using ShipEngine;

namespace Lab;

/// <summary>일상 물리 — 쏟음 (5단 C10 — docs/STAGE5.md): 마시기는 월드 요청 하나 (물탱크에서 빼고 · 쏟음을 셈한다) · 냄비 쏟음 · 젖은 바닥 미끄러짐 (C12) · 젖는 방 바닥 · 배수구 (C14).</summary>
public static partial class Program
{
    private static void SpillRules()
    {
        if (!Do("일상 물리")) return;
        Console.WriteLine("\n[일상 물리 — 쏟음 (C10)]");

        // (가) 컵: 몫 1 · 목마른 이가 물을 마시면 — 장부 마셨다 + 쏟았다 = 이번에 뺀 물 · 바닥 물이 쏟은 만큼 · 사실 '쏟았다' · 원인 '컵을 쏟았다'
        {
            var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
            var s = Worlds.Full(lay, 1);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var f = s.Get<Field>();
            int p = m.AddPerson(s, Worlds.At(lay, 3, 7));
            s.Run(2);
            life.CupFumble = 1;
            double Floor() { double w = 0; for (int c = 0; c < lay.N; c++) w += f.Stored(f.T.sWater, c); return w; }
            long d0 = m.Drinks; double drunk0 = m.WaterDrunk;
            double bd0 = s.Book.Of(Res.Water, Flow.Drunk), bs0 = s.Book.Of(Res.Water, Flow.Spilled), floor0 = Floor();
            for (int k = 0; k < (int)Time.Minutes(20) && m.Drinks == d0; k++)
            {
                ref var c = ref m[p].C; c.Thirst = 1; c.Hunger = 0; c.Sleepy = 0; c.Lonely = 0; c.Bored = 0;
                s.Step();
            }
            double drunk = m.WaterDrunk - drunk0, bookDrunk = bd0 - s.Book.Of(Res.Water, Flow.Drunk), bookSpill = bs0 - s.Book.Of(Res.Water, Flow.Spilled), floor = Floor() - floor0;
            var fact = s.Facts.Since(0).LastOrDefault(x => x.Kind == FactKind.Spilled);
            string why = fact.Kind == FactKind.Spilled ? s.Causes.Text(fact.Cause) : "";
            double taken = bookDrunk + bookSpill;
            Check("쏟음 (C10) 컵: 몫 1 · 목마른 이가 물을 마시면 장부 '마셨다' + '쏟았다' = 이번에 뺀 물 (쏟은 몫 40~100%) · 바닥 물이 쏟은 만큼 · 사실 '쏟았다' (그 몸 · 쏟은 몰) · 원인 '컵을 쏟았다'",
                m.Drinks == d0 + 1 && bookSpill >= 0.4 * taken - 1e-9 && bookSpill <= taken + 1e-9 && Math.Abs(bookDrunk - drunk) < 1e-9 && Math.Abs(floor - bookSpill) < 0.01 * bookSpill + 1e-6
                && fact.Kind == FactKind.Spilled && fact.Actor == m[p].C.Body && Math.Abs(fact.Size - bookSpill) < 1e-3 && why.Contains("컵을 쏟았다"),
                $"마심 {m.Drinks - d0} · 장부 마셨다 {bookDrunk:0.0} · 쏟았다 {bookSpill:0.0}몰 · 바닥 물 +{floor:0.00} · 사실 {(fact.Kind == FactKind.Spilled ? $"{fact.Size:0.0}몰 몸 {fact.Actor}" : "없음")} · '{why}'");
        }

        // (C10b) 컵을 들고 마신다: 식당 탁자 위 컵 하나 (꾸밈 · 물 5몰) · 목마른 이 — 컵을 집어 배식기에서 마시고 제자리 (같은 탁자 윗면 · 꾸밈)에 내려놓는다 · 물건 수 그대로
        //     (나) 컵이 없으면 예전 길로 마신다 · (다) 몫 1로 쏟으면 든 컵이 손에서 떨어진다 (손이 빈다 · 탁자 꾸밈이 아니다 · 물건 수 = 처음 + 장부 흐름)
        {
            const string CupShip = @"
###########
#.........#
#..t...m..#
#.........#
###########";
            (long drinks, bool took, bool back, int n0, int n1, double flow, bool empty, bool onTable, double w0, double w1) Cup(bool withCup, bool fumble)
            {
                var lay = Worlds.Ascii(CupShip, new Dictionary<char, string> { ['t'] = "Table", ['m'] = "MealDispenser" });
                lay.Rooms[lay.Room[Worlds.At(lay, 1, 1)]].Type = "Mess";
                var s = Worlds.Full(lay, 3);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var st = s.Get<Structure>(); var f = s.Get<Field>(); var net = s.Get<Network>();
                int cupItem = m.ItemNames.ToList().IndexOf("컵");
                int table = lay.Furniture.First(x => x.Type == "Table").Id;
                int id = -1;
                if (withCup)
                {
                    id = st.PutItem(cupItem, 0, Worlds.At(lay, 3, 2), 0, 5);
                    int i = st.IndexOf(id); var pr = st.Props[i]; pr.Flags |= Structure.DecorFlag; pr.Asleep = 1; st.Props[i] = pr;
                }
                int p = m.AddPerson(s, Worlds.At(lay, 1, 1));
                s.Run(2);
                if (fumble) life.CupFumble = 1;
                double Booked() => Enumerable.Range(0, Book.Flows).Sum(fl => cupItem < s.Book.Items ? s.Book.ItemFlow[cupItem * Book.Flows + fl] : 0);
                int n0 = m.CountAll(cupItem); double b0 = Booked();
                long d0 = m.Drinks; bool took = false;
                for (int k = 0; k < (int)Time.Minutes(20) && m.Drinks == d0; k++)
                {
                    ref var c = ref m[p].C; c.Thirst = 1; c.Hunger = 0; c.Sleepy = 0; c.Lonely = 0; c.Bored = 0;
                    s.Step();
                    took |= withCup && st.IndexOf(id) < 0 && m[p].C.Holding == cupItem;
                }
                life.CupFumble = 0;
                for (int k = 0; k < (int)Time.Minutes(3); k++) { ref var c = ref m[p].C; c.Thirst = 0; c.Hunger = 0; c.Sleepy = 0; c.Lonely = 0; c.Bored = 0; s.Step(); }
                var cups = st.Props.Where(x => x.Kind == 0 && x.What == cupItem).ToList();
                bool back = cups.Count == 1 && (cups[0].Flags & Structure.DecorFlag) != 0 && cups[0].On == table + 1 && cups[0].State == 0;
                bool onTable = cups.Any(x => x.On == table + 1 && (x.Flags & Structure.DecorFlag) != 0);
                return (m.Drinks - d0, took, back, n0, m.CountAll(cupItem), Booked() - b0, m[p].C.Holding == 255, onTable, withCup ? 5 : 0, cups.Sum(x => (double)x.Liquid) + m.HeldWater);
            }
            var a = Cup(true, false); var b = Cup(false, false); var c3 = Cup(true, true);
            Check("컵 (C10b) (가): 탁자 위 컵 — 목마른 이가 컵을 집어 배식기에서 마시고 같은 탁자 윗면에 꾸밈으로 내려놓는다 (담긴 물과 함께) · 물건 수 그대로 · 손이 빈다",
                a.drinks == 1 && a.took && a.back && a.n1 == a.n0 && a.empty && Math.Abs(a.w1 - a.w0) < 1e-6,
                $"마심 {a.drinks} · 집음 {a.took} · 제자리 {a.back} · 컵 {a.n0}→{a.n1} · 빈손 {a.empty} · 컵에 담긴 물 {a.w0:0.00}→{a.w1:0.00}몰");
            Check("컵 (C10b) (나): 컵이 없는 배 — 예전 길로 배식기에서 마신다", b.drinks == 1 && b.empty, $"마심 {b.drinks} · 빈손 {b.empty}");
            // (라) 바닥에 놓인 컵 (덤벙대는 이가 내려놓았다): 꼼꼼한 이는 주워 탁자 빈 자리에 꾸밈으로 둔다 (예전: 컵은 둘 곳이 없어 아무도 줍지 않았다)
            {
                var lay = Worlds.Ascii(CupShip, new Dictionary<char, string> { ['t'] = "Table", ['m'] = "MealDispenser" });
                lay.Rooms[lay.Room[Worlds.At(lay, 1, 1)]].Type = "Mess";
                var s = Worlds.Full(lay, 3);
                var m = s.Get<Mind>(); var st = s.Get<Structure>();
                int cupItem = m.ItemNames.ToList().IndexOf("컵");
                int table = lay.Furniture.First(x => x.Type == "Table").Id;
                st.PutItem(cupItem, 0, Worlds.At(lay, 5, 3), 0, 2);
                int p = m.AddPerson(s, Worlds.At(lay, 1, 1));
                m.SetPersonality(p, 0.5f, 0.95f, 0.5f, 0.5f, 0.3f);
                s.Run(2);
                for (int k = 0; k < (int)Time.Minutes(10); k++) { ref var c = ref m[p].C; c.Thirst = 0; c.Hunger = 0; c.Sleepy = 0; c.Lonely = 0; c.Bored = 0; s.Step(); }
                var cups = st.Props.Where(x => x.Kind == 0 && x.What == cupItem).ToList();
                bool ok = cups.Count == 1 && cups[0].On == table + 1 && (cups[0].Flags & Structure.DecorFlag) != 0 && Math.Abs(cups[0].Liquid - 2) < 1e-6 && m[p].C.Holding == 255;
                Check("컵 (C10b) (라): 바닥에 놓인 컵은 꼼꼼한 이가 주워 탁자 빈 자리에 꾸밈으로 둔다 (담긴 물과 함께)",
                    ok, $"컵 {cups.Count} · {(cups.Count > 0 ? $"받침 {cups[0].On} (탁자 {table + 1}) · 꾸밈 {(cups[0].Flags & Structure.DecorFlag) != 0} · 물 {cups[0].Liquid:0.0}" : "")} · 손 {m[p].C.Holding}");
            }
            Check("컵 (C10b) (다): 몫 1로 쏟으면 든 컵이 손에서 떨어진다 — 손이 빈다 · 탁자 꾸밈으로 돌아가지 않는다 · 물건 수 = 처음 + 장부 흐름",
                c3.drinks == 1 && c3.took && c3.empty && !c3.onTable && Math.Abs(c3.n1 - c3.n0 - c3.flow) < 1e-9,
                $"마심 {c3.drinks} · 집음 {c3.took} · 빈손 {c3.empty} · 탁자 {c3.onTable} · 컵 {c3.n0}→{c3.n1} (장부 {c3.flow:+0;-0;0}) · 컵에 남은 물 {c3.w1:0.00}몰");
        }

        // (C13) 조심 걸음: 복도 가운데 여섯 줄에 60몰 (100몰 아래 — 보이지 않는다) · 미끄러짐 몫 0.5 · 오른쪽 끝의 여섯이 목말라 왼쪽 배식기로 그 칸을 지난다 · 셋에게만 그 칸 믿음 (들은 것)
        //     모르는 셋 미끄러짐 ≥ 2 · 아는 셋은 절반 이하 · 아는 셋은 그 칸에서 더 오래 (조심해 느리게 — 1.3배 넘게) · 미끄러진 이는 그 칸을 느낀 것으로 배운다 · 아는 이의 믿음은 그 칸을 지난 뒤에도 남는다
        {
            var lay = Worlds.Ascii("\n####################\n#m.................#\n#pb.................#\n####################".Replace("#pb.................#", "#pb................#"), new Dictionary<char, string> { ['m'] = "MealDispenser", ['p'] = "PowerPanel", ['b'] = "Battery" });
            int[] wet = Enumerable.Range(5, 6).SelectMany(x => new[] { Worlds.At(lay, x, 1), Worlds.At(lay, x, 2) }).ToArray();   // 가운데 여섯 줄 (빠르기를 틱 단위로 잴 만큼)
            (int slipKnow, int slipNot, double dKnow, double dNot, bool learned, bool kept, int nSlipped) Run(ulong seed, bool slippery)
            {
                var s = Worlds.Full(lay, seed);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var f = s.Get<Field>();
                var ps = new[] { (16, 1), (16, 2), (17, 1), (17, 2), (18, 1), (18, 2) }.Select(q => m.AddPerson(s, Worlds.At(lay, q.Item1, q.Item2))).ToArray();
                s.Run(2);
                int spillCause = s.Causes.Add(CauseKind.Setup, "시험: 복도에 물이 고였다", s.Tick);
                foreach (int c in wet) f.AddStore(c, f.T.sWater, 60, spillCause);
                life.FloorSlipChance = slippery ? 0.5 : 0; life.FloorSlipMax = slippery ? 0.5 : 0;   // 미끄러지지 않는 판은 빠르기만 잰다
                var knows = new[] { ps[0], ps[3], ps[4] };
                var not = ps.Where(p => !knows.Contains(p)).ToArray();
                foreach (int p in knows) foreach (int x in new[] { 5, 10 }) m.Learn(m[p], s.Tick, BeliefKind.Water, Worlds.At(lay, x, 1), -1, 0.8f, BeliefSource.Heard);   // 들은 웅덩이 둘 (두 끝 — 믿음 하나가 둘레 두 칸)
                long fact0 = s.Facts.Total;
                var tIn = ps.ToDictionary(p => p, _ => -1L); var tOut = ps.ToDictionary(p => p, _ => -1L);   // 젖은 칸 앞 (x 11) → 지나 (x 4) 걸린 틱
                for (int k = 0; k < (int)Time.Minutes(10); k++)
                {
                    foreach (int p in ps) { ref var c = ref m[p].C; c.Sleepy = 0; c.Lonely = 0; c.Bored = 0; c.Hunger = 0; c.Unfit = 0; c.Thirst = c.DrankAt > 0 ? 0 : 1; }
                    s.Step();
                    foreach (int p in ps) { int x = lay.X(m[p].C.Cell) - 1; if (tIn[p] < 0 && x <= 11) tIn[p] = s.Tick; if (tOut[p] < 0 && x <= 4) tOut[p] = s.Tick; }
                }
                var fell = s.Facts.Since(fact0).Where(x => x.Kind == FactKind.Fell).ToList();
                bool Fell(int p) => fell.Any(x => x.Actor == p || x.Actor == m[p].C.Body);
                int Slips(IEnumerable<int> who) => fell.Count(x => who.Any(p => x.Actor == p || x.Actor == m[p].C.Body));
                double Dwell(IEnumerable<int> who) { var d = who.Where(p => tIn[p] >= 0 && tOut[p] >= 0).Select(p => (double)(tOut[p] - tIn[p])).ToList(); return d.Count == 0 ? 0 : d.Average(); }
                var slipped = not.Where(Fell).ToList();
                bool learned = slipped.Count > 0 && slipped.All(p => m[p].Beliefs.Any(b => b.Kind == BeliefKind.Water && b.Src == BeliefSource.Felt && b.Conf > 0.5f && Array.IndexOf(wet, b.Cell) >= 0));
                bool kept = knows.All(p => wet.Any(c => m.Believes(m[p], BeliefKind.Water, c, 2) >= 0.3f));
                return (Slips(knows), Slips(not), Dwell(knows.Where(p => !Fell(p))), Dwell(not.Where(p => !Fell(p))), learned, kept, slipped.Count);
            }
            var runs = new[] { 11UL, 12UL, 13UL }.Select(x => Run(x, true)).ToList();
            var calm = Run(11, false);   // 빠르기: 아무도 넘어지지 않는 판에서 그 칸에 머문 틱 / 들어섬
            int sk = runs.Sum(r => r.slipKnow), sn = runs.Sum(r => r.slipNot);
            double dk = calm.dKnow, dn = calm.dNot;
            Check("조심 걸음 (C13): 보이지 않는 고인 물 · 미끄러짐 몫 0.5 · 씨 셋 — 모르는 이 ≥ 2번 · 아는 이는 절반 이하 · 아는 이는 그 칸을 1.3배 넘게 오래 걸려 지난다 (조심해 느리게) · 미끄러진 이는 그 칸을 느낀 것으로 배우고 · 아는 이의 믿음은 그 칸을 지나고도 남는다",
                sn >= 2 && sk * 2 <= sn && dk >= 1.3 * dn && runs.All(r => r.nSlipped == 0 || r.learned) && runs.Any(r => r.nSlipped > 0) && runs.All(r => r.kept),
                $"미끄러짐 모름 {sn} · 앎 {sk} · 미끄러지지 않는 판 젖은 칸을 지난 틱 앎 {dk:0.00} · 모름 {dn:0.00} · 배움 {string.Join("/", runs.Select(r => $"{r.learned}({r.nSlipped})"))} · 믿음 남음 {string.Join("/", runs.Select(r => r.kept))}");
        }

        // (C15) 주컴퓨터 청소는 보이는 방만: 오른쪽 위 방에 고인 물 50몰 — 그 방 차단기가 내려가 (전기 없는 카메라) 보지 못하면 청소 일감 0 · 보이면 1 (예전: 카메라 없이 바닥 물을 읽었다)
        {
            int CleanJobs(bool blind)
            {
                var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
                var s = Worlds.Full(lay, 4);
                var f = s.Get<Field>(); var net = s.Get<Network>(); var sm = s.Get<ShipMind>();
                s.Get<Mind>().AddRobot(s, Worlds.At(lay, 21, 4), AgentKind.Robot, Worlds.At(lay, 21, 4));   // 청소할 로봇 (독 r)
                s.Run(3);
                int wet = Worlds.At(lay, 15, 2), room = lay.Room[wet];
                void Dark() { if (blind) net.Breaker[net.RoomFeed(room)] = 0; }   // 그 방 급전선 차단기가 내려갔다 — 전기가 없는 방의 카메라는 보지 못한다 (주컴퓨터가 다시 올리지 않게 틱마다)
                Dark();
                f.AddStore(wet, f.T.sWater, 50, s.Causes.Add(CauseKind.Setup, "시험: 바닥에 물이 고였다", s.Tick, wet));
                int n = 0;
                for (int k = 0; k < (int)Time.Minutes(6); k++) { Dark(); s.Step(); n = Math.Max(n, sm.Jobs.Count(j => j.Kind == GoalKind.Clean && lay.Room[j.Target] == room)); }
                return blind && sm.RoomSeen[room] != 0 ? -1 : n;
            }
            int dark = CleanJobs(true), lit = CleanJobs(false);
            Check("주컴퓨터 청소 (C15): 보지 못하는 방 (전기가 끊겼다)의 고인 물은 청소 일감 0 · 보이는 방은 1", dark == 0 && lit == 1, $"못 봄 {dark} (−1 = 그 방이 보였다) · 봄 {lit}");
        }

        // (C11) 쏟은 이: 컵을 한 번 쏟으면 (그 뒤 몫 0) — 성실 1.0인 이는 5분 안에 닦아 바닥 물 < 2몰 · 오수가 는다 · 성실 0.0인 이는 10분 뒤에도 쏟은 몫의 80% 넘게 남는다 (예전: 아무도 닦지 않았다)
        {
            (double spilled, double at5, double at10, double waste, float careful) Spill(float consc)
            {
                var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
                var s = Worlds.Full(lay, 1);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var f = s.Get<Field>(); var net = s.Get<Network>();
                int p = m.AddPerson(s, Worlds.At(lay, 3, 7));
                m.SetPersonality(p, 0.5f, consc, 0.5f, 0.5f, 0.3f);
                s.Run(2);
                life.CupFumble = 1;
                double Floor() { double w = 0; for (int c = 0; c < lay.N; c++) w += f.Stored(f.T.sWater, c); return w; }
                void Calm(bool thirsty) { ref var c = ref m[p].C; c.Thirst = thirsty ? 1 : 0; c.Hunger = 0; c.Sleepy = 0; c.Lonely = 0; c.Bored = 0; }
                long d0 = m.Drinks; double floor0 = Floor(), spill0 = s.Book.Of(Res.Water, Flow.Spilled);
                for (int k = 0; k < (int)Time.Minutes(20) && m.Drinks == d0; k++) { Calm(true); s.Step(); }
                life.CupFumble = 0;
                float careful = m[p].C.Careful;   // 쏟은 그 틱 (조심은 시간이 지나며 잦아든다)
                double spilled = spill0 - s.Book.Of(Res.Water, Flow.Spilled), waste0 = net.WasteWater, at5 = 0;
                for (int k = 0; k < (int)Time.Minutes(10); k++) { Calm(false); s.Step(); if (k == (int)Time.Minutes(5)) at5 = Floor() - floor0; }
                return (spilled, at5, Floor() - floor0, net.WasteWater - waste0, careful);
            }
            var hi = Spill(1f); var lo = Spill(0f);
            Check("쏟은 이 (C11): 컵을 쏟으면 성실한 이 (1.0)는 5분 안에 닦아 바닥 물 < 2몰 · 오수가 는다 · 성실하지 않은 이 (0.0)는 10분 뒤에도 쏟은 몫의 80% 넘게 남는다 · 둘 다 조심 +0.1",
                hi.spilled > 2 && hi.at5 < 2 && hi.waste > 0 && lo.spilled > 2 && lo.at10 > 0.8 * lo.spilled && hi.careful >= 0.099f && lo.careful >= 0.099f,
                $"성실: 쏟음 {hi.spilled:0.0}몰 · 5분 뒤 바닥 {hi.at5:0.0} · 오수 +{hi.waste:0.0} · 조심 {hi.careful:0.000} / 안 성실: 쏟음 {lo.spilled:0.0} · 10분 뒤 바닥 {lo.at10:0.0} · 조심 {lo.careful:0.000}");
        }

        // (나) 냄비: 몫 1 · 80℃ 냄비를 쏟으면 냄비 물이 바닥으로 (몫만큼 · 물의 합은 같다) · 쏟은 이가 덴다 (원인 '냄비를 쏟았다') — 30℃ 냄비는 데지 않는다
        {
            var legend = Worlds.MindLegend; legend['k'] = "Stove";
            var lay = Worlds.Ascii(Worlds.MindShip.Replace("#m.r..........w.....m...#", "#m.r..........w.....mk..#"), legend);
            (double moved, double floor, float burn, string why) Pan(float panK)
            {
                var s = Worlds.Full(lay, 1);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var net = s.Get<Network>(); var f = s.Get<Field>();
                s.Run(2);
                var stove = net.Devices.First(d => d.Def.Role == "stove");
                int p = m.AddPerson(s, stove.Cell), body = m[p].C.Body;
                net.FillPan(s, stove.Id);
                net.PanK[stove.Id] = panK;
                life.PanFumble = 1;
                double Floor() { double w = 0; for (int c = 0; c < lay.N; c++) w += f.Stored(f.T.sWater, c); return w; }
                double pan0 = net.PanWater[stove.Id], floor0 = Floor(); float burn0 = life[body].Burn;
                life.HandlePan(s, body, stove.Id, 0, 0, 0, p, m.NameOf(p));
                var fact = s.Facts.Since(0).LastOrDefault(x => x.Kind == FactKind.Spilled);
                return (pan0 - net.PanWater[stove.Id], Floor() - floor0, life[body].Burn - burn0, fact.Kind == FactKind.Spilled && fact.Other == stove.Furniture ? s.Causes.Text(fact.Cause) : "");   // 사실의 설비는 가구 번호 (컵과 같게 — 검토 고침)
            }
            var hot = Pan(353.15f); var warm = Pan(303.15f);
            Check("쏟음 (C10) 냄비: 몫 1 · 80℃ 냄비를 쏟으면 냄비 물 40~100%가 바닥으로 (빠진 만큼 바닥에 — 합이 같다) · 쏟은 이가 덴다 · 원인 '냄비를 쏟았다' / 30℃ 냄비는 쏟아도 데지 않는다",
                hot.moved >= 0.4 * Network.PanFill - 1e-3 && Math.Abs(hot.floor - hot.moved) < 1e-6 && hot.burn > 0 && hot.why.Contains("냄비를 쏟았다") && warm.moved > 0 && warm.burn == 0,
                $"80℃: 쏟음 {hot.moved:0.0}몰 · 바닥 +{hot.floor:0.0} · 데임 {hot.burn:0.000} · '{hot.why}' / 30℃: 쏟음 {warm.moved:0.0} · 데임 {warm.burn:0.000}");
        }

        // (가-2) 검토 고침: 배급이 있으면 쏟은 물도 그날 몫에서 빠진다 (마신 이가 쏟은 것을 안다 — 받은 물이 그만큼) · 몸이 칸 밖이면 (−1) 쏟지 않는다 (예전: 장부에만 '쏟았다'가 적히고 물은 어디에도 없었다) ·
        //       사실 '쏟았다'의 설비는 컵 · 냄비 모두 가구 번호
        {
            var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
            var s = Worlds.Full(lay, 1);
            var m = s.Get<Mind>(); var life = s.Get<Life>();
            int p = m.AddPerson(s, Worlds.At(lay, 3, 7));
            s.Run(2);
            life.CupFumble = 1;
            m.SetRation(0, 1000, 0);
            long d0 = m.Drinks;
            double bd0 = s.Book.Of(Res.Water, Flow.Drunk), bs0 = s.Book.Of(Res.Water, Flow.Spilled), today0 = m.DrankToday(p);
            for (int k = 0; k < (int)Time.Minutes(20) && m.Drinks == d0; k++) { ref var c = ref m[p].C; c.Thirst = 1; c.Hunger = 0; c.Sleepy = 0; s.Step(); }
            double took = (bd0 - s.Book.Of(Res.Water, Flow.Drunk)) + (bs0 - s.Book.Of(Res.Water, Flow.Spilled)), today = m.DrankToday(p) - today0;
            var body = m[p].C.Body;
            int keep = life[body].Cell; life[body].Cell = -1;
            double bs1 = s.Book.Of(Res.Water, Flow.Spilled);
            var off = life.Drink(s, body, 0, 20, 0, 0, 0, p, m.NameOf(p));
            bool offOk = off.spilled == 0 && s.Book.Of(Res.Water, Flow.Spilled) == bs1;
            life[body].Cell = keep;
            Check("쏟음 (C10 검토 고침): 배급이 있으면 쏟은 물도 그날 몫에서 빠진다 (받은 물 = 마신 + 쏟은) · 몸이 칸 밖이면 쏟지 않는다 (장부에만 적히고 물이 없던 것)",
                m.Drinks == d0 + 1 && took > 0 && Math.Abs(today - took) < 1e-9 && offOk,
                $"받은 물 {took:0.0}몰 · 그날 몫에서 빠진 {today:0.0} · 칸 밖 쏟음 {off.spilled:0.0} (장부 {(offOk ? "그대로" : "적힘")})");
        }

        // (다) C12 젖은 바닥 미끄러짐: 복도 한 줄 — 지린 물 (90몰 — 보이지 않는다)이 고인 칸을 B가 목말라 지나가면 (몫 1) 넘어진다 — 사실 '넘어졌다' 하나 · 원인 '미끄러져' ← '지렸다' · 타박 · Slips 1 · Falls 0
        //     같은 차림 무중력 칸 · 로봇은 미끄러지지 않는다
        {
            var lay = Worlds.Ascii("\n##############\n#m..........e#\n##############", new Dictionary<char, string> { ['m'] = "MealDispenser", ['e'] = "Bed" });
            (int fell, string story, float blunt, long slips, long falls) Slip(bool zeroG, bool robot)
            {
                var s = Worlds.Full(lay, 7);
                var m = s.Get<Mind>(); var life = s.Get<Life>(); var f = s.Get<Field>();
                int wet = Worlds.At(lay, 6, 1);
                int b = robot ? m.AddRobot(s, Worlds.At(lay, 11, 1)) : m.AddPerson(s, Worlds.At(lay, 11, 1));
                s.Run(2);
                f.AddStore(wet, f.T.sWater, 90, s.Causes.Add(CauseKind.Biology, "참지 못하고 옷에 지렸다 (방광이 찼다)", s.Tick, wet));   // A가 그 칸에서 지렸다 (C8 원인) — 100몰 아래 (보이지 않는 물기 · C13 뒤: 보이는 물은 조심해 디딘다 — 아래 '조심 걸음')
                if (zeroG) for (int c = 0; c < lay.N; c++) f.Grav[c] = 0.1f;
                life.FloorSlipChance = 1; life.FloorSlipMax = 1;
                float blunt0 = robot ? 0 : life[m[b].C.Body].Blunt;
                long slips0 = life.Slips, falls0 = m.Falls, fact0 = s.Facts.Since(0).Count(x => x.Kind == FactKind.Fell);
                for (int k = 0; k < (int)Time.Minutes(10) && life.Slips == slips0; k++)
                {
                    if (!robot) { ref var c = ref m[b].C; c.Thirst = 1; c.Hunger = 0; c.Sleepy = 0; }
                    s.Step();
                }
                if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") Console.WriteLine($"      물 {string.Join(" ", Enumerable.Range(1, 12).Select(x => f.Stored(f.T.sWater, Worlds.At(lay, x, 1)).ToString("0")))} · B 칸 ({lay.X(m[b].C.Cell)}) 목표 {m[b].C.Goal} · 마심 {m.Drinks}");
                var fell = s.Facts.Since(0).Where(x => x.Kind == FactKind.Fell).Skip((int)fact0).ToList();
                return (fell.Count, fell.Count > 0 ? s.Causes.Story(fell[0].Cause) : "", robot ? 0 : life[m[b].C.Body].Blunt - blunt0, life.Slips - slips0, m.Falls - falls0);
            }
            var one = Slip(false, false); var low = Slip(true, false); var bot = Slip(false, true);
            Check("미끄러짐 (C12): 복도에서 지린 물이 고인 칸을 B가 목말라 지나가면 (몫 1) 미끄러져 넘어진다 — 사실 '넘어졌다' 하나 · 원인 '젖은 바닥에 미끄러져' ← '지렸다' · 타박 · 미끄러짐 1 · 넘어짐 (점화 · 덜컹) 0 / 무중력 칸 · 로봇은 0",
                one.fell == 1 && one.story.Contains("젖은 바닥에 미끄러져") && one.story.Contains("지렸다") && one.blunt > 0 && one.slips == 1 && one.falls == 0 && low.slips == 0 && low.fell == 0 && bot.slips == 0,
                $"넘어짐 사실 {one.fell} · '{one.story}' · 타박 +{one.blunt:0.00} · 미끄러짐 {one.slips} · 넘어짐 셈 {one.falls} / 무중력 {low.slips} · 로봇 {bot.slips}");
        }

        // (라) C14 배수구: 화장실 (샤워 칸이 있는 방) 변기 칸에 1500몰 · 30분 — 젖는 방 바닥은 배수구 쪽으로 기울어 (막 5몰) 샤워 칸 배수구로 오수가 1200몰 넘게 늘고 ·
        //     화장실 바닥 물 합은 처음의 20% 아래 · 물의 합은 그대로 / 무중력이면 배수구가 빼지 않는다
        {
            var legend = new Dictionary<char, string> { ['e'] = "Bed", ['p'] = "PowerPanel", ['b'] = "Battery", ['T'] = "Toilet", ['S'] = "Shower", ['k'] = "Sink", ['o'] = "Composter" };
            var lay = Worlds.Ascii(WashCareShip.Replace('c', '.').Replace('r', '.'), legend);
            (double waste, double floor0, double floor1, double sum0, double sum1) Drain(bool zeroG)
            {
                var s = Worlds.Full(lay, 3);
                var net = s.Get<Network>(); var f = s.Get<Field>();
                s.Run(2);
                if (zeroG) for (int c = 0; c < lay.N; c++) f.Grav[c] = 0.1f;
                var toilet = lay.Furniture.Where(x => x.Type == "Toilet").OrderBy(x => lay.X(x.Cells[0])).First();
                int tc = toilet.Cells[0], room = lay.Room[tc];
                double Floor() { double w = 0; for (int c = 0; c < lay.N; c++) if (lay.Room[c] == room) w += f.Stored(f.T.sWater, c); return w; }
                double Sum() { double w = net.WaterTank + net.WasteWater + f.Total(f.T.gH2O); for (int c = 0; c < lay.N; c++) w += f.Stored(f.T.sWater, c) + f.Drops[f.T.sWater * lay.N + c]; foreach (var d in net.Devices) w += net.PanWater[d.Id]; return w; }
                f.AddStore(tc, f.T.sWater, 1500, s.Causes.Add(CauseKind.Setup, "시험: 변기 칸에 물이 쏟아졌다", s.Tick, tc));
                double waste0 = net.WasteWater, floor0 = Floor(), sum0 = Sum();
                s.Run(Time.Minutes(30));
                return (net.WasteWater - waste0, floor0, Floor(), sum0, Sum());
            }
            var g1 = Drain(false); var g0 = Drain(true);
            Check("배수구 (C14): 화장실 변기 칸에 1500몰 · 30분 — 젖는 방 바닥이 배수구 쪽으로 기울어 샤워 칸 배수구로 오수 +1200몰 넘게 · 화장실 바닥 물이 처음의 20% 아래 · 물의 합 그대로 / 무중력이면 배수구가 빼지 않는다",
                g1.waste >= 1200 && g1.floor1 <= 0.2 * g1.floor0 && Math.Abs(g1.sum1 - g1.sum0) < 1e-6 * g1.sum0 && g0.waste < 1,
                $"오수 +{g1.waste:0} · 화장실 바닥 {g1.floor0:0} → {g1.floor1:0} · 합 {g1.sum0:0.000} → {g1.sum1:0.000} / 무중력 오수 +{g0.waste:0.0}");
        }
    }
}
