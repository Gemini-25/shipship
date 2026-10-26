using ShipEngine;

namespace Lab;

/// <summary>물리 세계 뒤 커밋 (docs/PHYSICS.md §11 PH11~PH16) — 우주의 물체 · 물체 볼거리 · 판단 · 몸 물체 · 판 조각.</summary>
public static partial class Program
{
    private static void Physics2Rules()
    {
        // ── PH11 우주의 물체: 외판 구멍 밖으로 나간 수리 꾸러미는 틱마다 배에서 멀어지고 · 300m 뒤 장부 '우주로' (예전: 나간 그 틱에 장부로 지워졌다) ──
        if (Do("우주의 물체"))
        {
            Console.WriteLine("\n[우주의 물체 — 구멍 밖 물건이 멀어진다]");
            var lay = Worlds.Ascii(FlingRooms, Worlds.Seats);
            var s = Worlds.Full(lay, 1);
            var st = s.Get<Structure>(); var m = s.Get<Mind>();
            byte kit = (byte)m.ItemIndex("수리 꾸러미");
            Worlds.PutItem(s, kit, Worlds.At(lay, 1, 2));
            st.RequestDoor(lay.DoorAt[Worlds.At(lay, 6, 2)], 1);
            s.Run(10);
            s.Get<Field>().SetHole(Worlds.At(lay, 0, 2), 1.5f);
            var dist = new List<double>(); long goneAt = -1; int id = -1;
            for (int k = 0; k < 120 && goneAt < 0; k++)
            {
                s.Step();
                int i = id > 0 ? st.IndexOf(id) : st.Props.FindIndex(p => Structure.Flying(p) && p.What == kit);
                if (i >= 0 && Structure.Flying(st.Props[i])) { var p = st.Props[i]; id = p.Id; var o = st.Bodies[0]; dist.Add(Math.Sqrt((p.X - o.X) * (p.X - o.X) + (p.Y - o.Y) * (p.Y - o.Y))); }
                else if (id > 0) goneAt = s.Tick;
            }
            bool away = dist.Count >= 3 && dist.Zip(dist.Skip(1)).All(z => z.Second > z.First);
            Check("우주의 물체: 외판 구멍 밖으로 나간 수리 꾸러미는 틱마다 배에서 멀어지고 (세 틱 넘게 날며) · 300m를 넘으면 장부 '우주로' (그때 목록에서 빠진다)",
                away && goneAt > 0 && dist[^1] < 300 && s.Book.OfItem(kit, Flow.Spaced) == -1 && !st.Props.Any(p => p.What == kit && p.Kind == 0),
                $"난 틱 {dist.Count} · 거리 {string.Join(" → ", dist.Take(6).Select(x => x.ToString("0")))}… {(dist.Count > 0 ? dist[^1] : 0):0}m · 우주로 틱 {goneAt} · 장부 {s.Book.OfItem(kit, Flow.Spaced)}");
        }

        // ── PH13 판단 — 물체: 날아드는 것을 비킨다 (보이면) · 로봇이 깨진 조각 · 가루를 치우고 넘어진 것을 세운다 (DF5 정돈 몫) ──
        if (Do("판단 — 물체"))
        {
            Console.WriteLine("\n[판단 — 물체: 비키기 · 치우기 · 세우기]");
            var tall = Worlds.Ascii("\n#########\n#.......#\n#.......#\n#.......#\n#.......#\n#.......#\n#.......#\n#########");
            // 비키기: 남쪽을 보고 선 이에게 남쪽 4m에서 식량 상자가 6m/s로 — 밝으면 비켜 안 맞는다 · 등 뒤에서 오면 · 연기가 차면 못 비켜 맞는다
            (int hits, long dodges, float blunt) Throw(bool back, bool smoke)
            {
                var s = Worlds.Full(tall, 1);
                var m = s.Get<Mind>(); var st = s.Get<Structure>(); var life = s.Get<Life>(); var f = s.Get<Field>();
                int who = m.AddPerson(s, Worlds.At(tall, 4, 1)), body = m[who].C.Body;
                s.Run(5);   // 빛을 한 번 잰 뒤 (빛은 5틱마다)
                if (back) { m[who].C.FaceX = 0; m[who].C.FaceY = -1; }
                if (smoke) foreach (int c in tall.Rooms[0].Cells) f.AddGas(c, f.T.gSoot, 0.3, 293);
                int id = Worlds.PutItem(s, "식량 상자", Worlds.At(tall, 4, 6));
                var p = st.Props[st.IndexOf(id)]; var fm = st.PropForm(p);
                st.Shove(id, 0, -fm.Mass * 7, 0, s.Causes.Add(CauseKind.Setup, "누가 식량 상자를 걷어찼다", s.Tick));
                int n = 0;
                for (int k = 0; k < 3; k++) { s.Step(); n = Math.Max(n, st.PropHits.Count(h => h.Tag == body)); }
                s.Run(5);
                return (n, m.Dodges, life[body].Blunt);
            }
            var lit = Throw(false, false); var behind = Throw(true, false); var smoky = Throw(false, true);
            Check("비키기: 남쪽을 보고 선 이에게 남쪽 5m에서 식량 상자가 7m/s로 날아들면 옆 칸으로 비켜 안 맞는다 · 등 뒤에서 오거나 연기가 차 안 보이면 못 비켜 맞는다",
                lit.hits == 0 && lit.dodges >= 1 && lit.blunt == 0 && behind.hits > 0 && behind.dodges == 0 && smoky.hits > 0 && smoky.dodges == 0,
                $"밝음: 맞음 {lit.hits} · 비킴 {lit.dodges} · 타박 {lit.blunt:0.00} / 등 뒤: 맞음 {behind.hits} · 비킴 {behind.dodges} / 연기: 맞음 {smoky.hits} · 비킴 {smoky.dodges}");

            // 치우기 · 세우기: 사람 없는 배 · 로봇 하나 — 떨어져 깨진 컵의 조각 · 가루와 누운 소화기를 로봇이 맡아 치우고 세운다 (주컴퓨터가 카메라로 본다)
            {
                var t = Tables.Default;
                int cup = t.People.Items.Keys.ToList().IndexOf("컵"), ext = t.People.Items.Keys.ToList().IndexOf("소화기");
                var legend = new Dictionary<char, string> { ['e'] = "Bed", ['p'] = "PowerPanel", ['b'] = "Battery", ['r'] = "RobotDock", ['c'] = "MainComputer" };
                var lay = Worlds.Ascii("\n##############\n#e..........c#\n#............#\n#pb.........r#\n##############", legend);
                Sim? s = null; Structure? st = null; bool broke = false;
                for (ulong seed = 1; seed <= 10 && !broke; seed++)
                {
                    s = Worlds.Full(lay, seed);
                    st = s.Get<Structure>();
                    s.Get<Mind>().AddRobot(s, Worlds.At(lay, 11, 2), AgentKind.Robot, Worlds.At(lay, 11, 2));
                    s.Run(3);
                    st.PutItem(cup, 0, Worlds.At(lay, 6, 1), 0.75f);
                    s.Run(4);
                    broke = st.Shattered > 0;
                }
                int fx = st!.PutItem(ext, 1, Worlds.At(lay, 4, 2));
                { int i = st.IndexOf(fx); var q = st.Props[i]; q.Pose = 1; st.Props[i] = q; }   // 넘어져 누운 소화기
                int pieces0 = st.Props.Count(p => p.Kind == 2);
                float grit0 = st.Grit.Sum();
                s!.Run((int)Time.Minutes(60));
                int pieces = st.Props.Count(p => p.Kind == 2);
                int fallen = st.Props.Count(p => p.Kind == 0 && p.Pose != 0);
                Check("치우기 · 세우기 (정돈): 사람 없는 배에서 떨어져 깨진 컵의 조각 · 가루와 넘어져 누운 소화기를 — 주컴퓨터가 카메라로 보고 로봇에게 맡겨 한 시간 안에 쓸어 담고 세운다",
                    broke && pieces0 > 0 && pieces == 0 && st.Grit.Sum() == 0 && fallen == 0 && st.SweptPieces >= pieces0 && st.Righted >= 1,
                    $"깨짐 {broke} · 조각 {pieces0} → {pieces} · 가루 {grit0 * 1000:0}g → {st.Grit.Sum() * 1000:0}g · 누운 것 {fallen} · 쓸어 담음 {st.SweptPieces}조각 {st.SweptKg * 1000:0}g · 세움 {st.Righted}");
            }
        }

        // ── PH16 둘이서 들기: 넘어진 45kg 책장 — 로봇이 둘이면 주컴퓨터가 둘을 함께 보내 세우고 · 하나뿐이면 맡기지 않는다 (같은 실패를 되풀이하지 않게) ──
        if (Do("판단 — 둘이서 들기"))
        {
            Console.WriteLine("\n[판단 — 둘이서 들기 (PH16)]");
            var legend = new Dictionary<char, string> { ['e'] = "Bed", ['p'] = "PowerPanel", ['b'] = "Battery", ['r'] = "RobotDock", ['c'] = "MainComputer", ['k'] = "Bookshelf" };
            var lay = Worlds.Ascii("\n##############\n#e....k.....c#\n#............#\n#pb........rr#\n##############", legend);
            (bool up, int righted, int posted) Run(int robots)
            {
                var s = Worlds.Full(lay, 1);
                var st = s.Get<Structure>(); var m = s.Get<Mind>();
                for (int k = 0; k < robots; k++) m.AddRobot(s, Worlds.At(lay, 10 + k, 3), AgentKind.Robot, Worlds.At(lay, 10 + k, 3));
                s.Run(3);
                int shelf = lay.Furniture.FindIndex(f => f.Type == "Bookshelf");
                int id = st.TearLoose(s, shelf, 0, 0, 0, s.Causes.Add(CauseKind.Setup, "시험 — 책장이 넘어졌다", s.Tick), "볼트가 풀린");
                { int i = st.IndexOf(id); var q = st.Props[i]; q.X = Worlds.At(lay, 6, 2) % lay.W + 0.5; q.Y = lay.Y(Worlds.At(lay, 6, 2)) + 0.5; q.Pose = 1; q.State = 0; q.Vx = q.Vy = q.W = 0; st.Props[i] = q; st.ClutterChanged(); }
                int posted = 0;
                for (int k = 0; k < (int)Time.Minutes(90); k++)
                {
                    s.Step();
                    posted = Math.Max(posted, m.Agents.Count(a => !a.IsPerson && a.C.AssignedGoal == GoalKind.Clean));
                }
                int j = st.IndexOf(id);
                return (j >= 0 && st.Props[j].Pose == 0, (int)st.Righted, posted);
            }
            var two = Run(2); var one = Run(1);
            Check("둘이서 들기 (PH16): 넘어진 45kg 책장 (로봇 하나는 40kg까지) — 로봇이 둘이면 주컴퓨터가 둘을 함께 보내 한 시간 반 안에 세우고 · 하나뿐이면 맡기지 않는다",
                two.up && two.righted >= 1 && two.posted >= 2 && !one.up && one.posted == 0,
                $"로봇 둘: 세움 {two.up} ({two.righted}) · 함께 맡은 {two.posted} / 하나: 세움 {one.up} · 맡은 {one.posted}");
        }

        // ── 5단 C9 흔들림: 약 기운에 손이 무디다 — 같은 몸 둘 (하나만 진통제 기운 1) · 공구 미끄러질 몫 1로 한 번씩 ──
        if (Do("일상 물리 — 흔들림"))
        {
            Console.WriteLine("\n[일상 물리 — 흔들림 (C9)]");
            var lay = Worlds.Ascii(Worlds.Symmetric);
            var s = new Sim(new SimSetup(lay, 1, sim => { var ff = new Field(sim.Layout, sim.Tables); sim.Add(ff); sim.Add(new Life(ff)); }));
            s.Get<Field>().FillAir();
            var life = s.Get<Life>();
            int clear = life.AddBody(Worlds.At(lay, 3, 2)), drugged = life.AddBody(Worlds.At(lay, 7, 2));
            life[drugged].Opioid = 40;   // 진통제 기운 1 (약 기운 = 남은 양 / 40)
            double r = Life.MishapOdds(life[drugged], 0, 0) / Life.MishapOdds(life[clear], 0, 0);
            life.SlipChance = 1; life.ScaldChance = 0;
            int c1 = life.WorkHazard(s, clear, life[clear].Cell, 293, 0.4f, 0, 0, -1, "김하늘", "냉각 펌프", 0, life.Unsteady(clear), Life.MishapWhy(life[clear], 0));
            int c2 = life.WorkHazard(s, drugged, life[drugged].Cell, 293, 0.4f, 0, 0, -1, "이도윤", "냉각 펌프", 0, life.Unsteady(drugged), Life.MishapWhy(life[drugged], 0));
            string t1 = c1 != 0 ? s.Causes.Text(c1) : "", t2 = c2 != 0 ? s.Causes.Text(c2) : "";
            Check("흔들림 (C9): 같은 몸 둘 가운데 진통제 기운 1인 몸은 손이 미끄러질 몫이 1.9배 넘고 · 미끄러진 원인 글에 '약 기운에'가 붙는다 (맑은 몸에는 없다)",
                r >= 1.9 && t2.Contains("약 기운에") && t1.Length > 0 && !t1.Contains("약 기운"), $"몫 비 {r:0.00} · 맑은 몸 '{t1}' · 약 기운 '{t2}'");
        }

        // ── PH13 검토 고침: 세운 가구의 길 값 — 세운 뒤 길 값이 찍고 되살린 세계와 같다 (예전: 세워도 옛 길 값이 남아 되살린 세계와 달랐다) ──
        if (Do("판단 — 물체 길 값"))
        {
            var s = ShipWorld.Crewed("Didim", 7, 1, 0, 0);
            var st = s.Get<Structure>(); var L = s.Layout;
            bool Free(int x, int y) => L.In(x, y) && L.Kind[L.Index(x, y)] == CellKind.Floor && L.FurnitureAt[L.Index(x, y)] < 0;
            int mid = Enumerable.Range(0, L.N).First(c => L.Room[c] >= 0 && Enumerable.Range(-2, 5).All(dx => Free(L.X(c) + dx, L.Y(c)) && Free(L.X(c) + dx, L.Y(c) - 1) && Free(L.X(c) + dx, L.Y(c) + 1)));
            int shelf = Enumerable.Range(0, L.Furniture.Count).First(f => L.Furniture[f].Type == "Shelf");
            int id = st.TearLoose(s, shelf, 0, 0, 0, s.Causes.Add(CauseKind.Setup, "시험 — 선반이 넘어졌다", s.Tick), "볼트가 풀린");
            { int i = st.IndexOf(id); var q = st.Props[i]; q.X = L.X(mid) + 0.5; q.Y = L.Y(mid) + 0.5; q.Pose = 1; q.Th = 0.7; q.State = 0; q.Vx = q.Vy = q.W = 0; st.Props[i] = q; st.ClutterChanged(); }
            List<int> Cl(Structure x) => Enumerable.Range(0, L.N).Where(c => x.ClutterAt(c) > 0).ToList();
            var lying = Cl(st);
            var around = Enumerable.Range(-1, 3).SelectMany(dy => Enumerable.Range(-1, 3).Select(dx => L.Index(L.X(mid) + dx, L.Y(mid) + dy))).ToArray();
            int up = st.RightUp(around, 1000);
            var r = new Sim(s.Setup); r.Load(s.Save());
            var a = Cl(st); var b = Cl(r.Get<Structure>());
            Check("세운 가구의 길 값 (PH13 검토 고침): 누운 선반을 세우면 길 값이 선 꼴로 바뀌고 · 찍고 되살린 세계와 같다",
                up == 1 && a.SequenceEqual(b) && !a.SequenceEqual(lying), $"세움 {up} · 누운 길 값 {lying.Count}칸 → 지금 {a.Count}칸 · 되살린 {b.Count}칸");
            // (PH16) 깨진 유리 가루 · 판 조각이 흩어진 칸은 +2 — 같은 길이면 돌아간다 (바로 가면 넷 + 흩어진 셋 × 2 = 10 · 돌아가면 6)
            bool Clear(int x, int y) => Free(x, y) && st.ClutterAt(L.Index(x, y)) == 0;
            int mid2 = Enumerable.Range(0, L.N).First(c => L.Room[c] >= 0 && Enumerable.Range(-2, 5).All(dx => Clear(L.X(c) + dx, L.Y(c)) && Clear(L.X(c) + dx, L.Y(c) - 1) && Clear(L.X(c) + dx, L.Y(c) + 1)));
            int row = L.Y(mid2), x0 = L.X(mid2);
            st.EnsureBody();
            foreach (int dx in new[] { -1, 0 }) st.Grit[L.Index(x0 + dx, row)] += 1f;
            int plate = st.ThrowPlate(s, x0 + 1.5, row + 0.5, 0, 0, 20, 0);
            { int i = st.IndexOf(plate); var q = st.Props[i]; q.State = 0; q.Host = 0; st.Props[i] = q; st.ClutterChanged(); }
            var m = s.Get<Mind>();
            int who = m.Agents.First(x => x.IsPerson).Id;
            var path = m.PathOf(who, L.Index(x0 - 2, row), L.Index(x0 + 2, row));
            bool round = path.Count > 0 && !path.Contains(L.Index(x0 - 1, row)) && !path.Contains(L.Index(x0, row)) && !path.Contains(L.Index(x0 + 1, row));
            Check("깨진 것의 길 값 (PH16): 가루 1kg 두 칸 · 판 조각 한 칸이 흩어진 줄은 +2 — 같은 줄 양 끝을 오가는 이는 그 셋을 밟지 않고 옆 줄로 돌아간다",
                st.ClutterAt(L.Index(x0, row)) == Structure.LitterCost && st.ClutterAt(L.Index(x0 + 1, row)) == Structure.LitterCost && round,
                $"길 값 가루 {st.ClutterAt(L.Index(x0, row))} · 판 조각 {st.ClutterAt(L.Index(x0 + 1, row))} · 길 {path.Count}칸 · 돌아감 {round}");
        }

        // ── PH12 물체 볼거리: 카메라 안에서 컵이 깨지면 그 틱들을 천천히 (화면 1초에 1틱) — 하루 느린 예산 안 ──
        if (Do("물체 볼거리"))
        {
            Console.WriteLine("\n[물체 볼거리 — 깨지는 틱은 천천히]");
            var t = Tables.Default;
            int cup = t.People.Items.Keys.ToList().IndexOf("컵");
            var legend = new Dictionary<char, string> { ['e'] = "Bed", ['p'] = "PowerPanel", ['b'] = "Battery" };
            var lay = Worlds.Ascii(BreakShip, legend);
            Sim? s = null; Director? d = null; Fact fact = default; double before = 0, after = 0; long at = -1;
            for (ulong seed = 1; seed <= 10 && fact.Kind != FactKind.Shattered; seed++)
            {
                s = Worlds.Full(lay, seed);
                d = new Director(s.Layout) { KeepRing = false, DayMinutes = 3 };
                for (int k = 0; k < 5; k++) { s.Step(); d.Observe(s); }
                d.FocusCell = Worlds.At(lay, 8, 2);
                s.Get<Structure>().PutItem(cup, 0, Worlds.At(lay, 8, 1), 0.75f, 11);
                for (int k = 0; k < 6; k++)
                {
                    double sc = d.ScreenSeconds;
                    s.Step(); d.Observe(s);
                    if (fact.Kind != FactKind.Shattered) { fact = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Shattered); if (fact.Kind == FactKind.Shattered) { at = s.Tick; before = sc; } }
                }
                after = d.ScreenSeconds;
            }
            double shown = after - before;
            Check("물체 볼거리: 카메라 안에서 0.75m 떨어진 컵이 깨지면 그 뒤 틱들을 천천히 — 화면 2초 넘게 (조용함 빠르기면 0.1초 남짓 · 많아야 3틱 · 깨졌다의 무게 8)",
                fact.Kind == FactKind.Shattered && shown >= 2 && d!.PropScreenSec is >= 2 and <= 3.5 && Director.Weight(FactKind.Shattered) == 8,
                $"깨짐 {(fact.Kind == FactKind.Shattered ? $"틱 {fact.Tick}" : "없음")} · 그 뒤 화면 {shown:0.00}초 · 볼거리 {d?.PropScreenSec:0.00}초");
        }
    
        // ── PH14 몸 물체: 제 힘을 잃은 사람 · 로봇은 ④가 원판 물체로 셈한다 — 끌려가다 벽에 부딪혀 다치고 · 큰 틈으로 나간 이는 멀어지고 · 로봇도 미끄러진다 ──
        if (Do("몸 물체"))
        {
            Console.WriteLine("\n[몸 물체 — 끌려가는 몸 (PH14)]");
            // (가) 바람에 발이 떨어져 8m/s로 끌려간 이가 3.5m 앞 벽에 부딪힌다 — 남은 운동 에너지만큼 타박 (원인 '끌려가다 부딪혔다') · 멈추면 다시 제 힘으로
            {
                var lay = Worlds.Ascii(SpaceRoom);
                var s = Worlds.Full(lay, 1);
                var st = s.Get<Structure>(); var m = s.Get<Mind>(); var life = s.Get<Life>();
                int who = m.AddPerson(s, Worlds.At(lay, 4, 2));
                s.Run(2);
                int body = m[who].C.Body;
                st.Ragdoll(s, body, m[who].C.Cell, false, false, s.Causes.Add(CauseKind.Setup, "시험 — 센 바람에 발이 떨어졌다", s.Tick), 8, 0);
                s.Run(4);
                string story = s.Causes.Story(life[body].HurtCause);
                Check("몸 물체 (가): 8m/s로 끌려간 이가 3.5m 앞 벽에 부딪혀 다친다 (마찰을 빼고 남은 에너지 — 타박 · 원인 '끌려가다 부딪혔다') · 멈추면 몸 물체를 거두고 벽 곁 칸에 선다",
                    life[body].Blunt > 0.05f && story.Contains("끌려가다 부딪혔다") && st.RagdollOf(body) < 0 && m[who].C.Cell == Worlds.At(lay, 7, 2),
                    $"타박 {life[body].Blunt:0.00} · \"{story}\" · 몸 물체 {st.RagdollOf(body)} · 칸 x {lay.X(m[who].C.Cell)}");
            }
            // (다) 검토 고침: 끌려가는 동안 그 이는 아무것도 하지 않는다 (마음이 서 있다고 여겨 걷고 · 줍고 · 넘어짐을 거듭 세던 결함) · 무중력에서 끌려간 몸도 느려지면 다시 제 힘으로 (예전: 떠 있는 몸 물체가 끝나지 않았다)
            {
                var lay = Worlds.Ascii(SpaceRoom);
                (bool idle, long pulled, bool ended) Slide(bool zeroG)
                {
                    var s = Worlds.Full(lay, 1);
                    var st = s.Get<Structure>(); var m = s.Get<Mind>(); var f = s.Get<Field>();
                    int who = m.AddPerson(s, Worlds.At(lay, 2, 2));
                    void Grav() { if (zeroG) for (int c = 0; c < lay.N; c++) f.Grav[c] = 0; }
                    Grav(); s.Run(2);
                    int body = m[who].C.Body;
                    st.Ragdoll(s, body, m[who].C.Cell, false, false, s.Causes.Add(CauseKind.Setup, "시험 — 센 바람에 발이 떨어졌다", s.Tick), zeroG ? 1.5 : 8, 0);
                    bool idle = true, ended = false;
                    for (int k = 0; k < (zeroG ? 200 : 6); k++)
                    {
                        Grav(); s.Step();
                        if (st.RagdollOf(body) >= 0) idle &= m[who].C.Up == 0 && m[who].Path.Count == 0;
                        else { ended = true; break; }
                    }
                    return (idle, m.PeoplePulled, ended);
                }
                var g1 = Slide(false); var g0 = Slide(true);
                Check("몸 물체 (다) 검토 고침: 끌려가는 동안 그 이는 서지도 걷지도 않고 (넘어짐은 한 번) 멈추면 다시 제 힘으로 · 무중력에서 1.5m/s로 끌려간 몸도 느려지면 몸 물체를 거둔다 (200틱 안)",
                    g1.idle && g1.pulled == 1 && g1.ended && g0.idle && g0.pulled == 1 && g0.ended,
                    $"1g: 가만 {g1.idle} · 넘어짐 {g1.pulled} · 거둠 {g1.ended} / 무중력: 가만 {g0.idle} · 넘어짐 {g0.pulled} · 거둠 {g0.ended}");
            }
            // (라) 검토 고침: 장이 흐른 뒤 (이 틱 돌풍) 발이 떨어진 몸은 이번 틱 걷는 원판에서 빠진다
            {
                var lay = Worlds.Ascii(SpaceRoom);
                var s = FieldStructure(lay);
                var st = s.Get<Structure>(); var f = s.Get<Field>();
                s.Run(2);
                int c = Worlds.At(lay, 2, 2);
                f.AddStore(c, f.T.sWater, 3000);   // 젖은 바닥 (발 마찰 0.1 — 구멍 곁이 아니어도 돌풍에 발이 떨어진다)
                var who = new[] { new PropActor { Tag = 0, Cell = c, R = Structure.ActorR, Kg = 70, H = 1.7f } };
                st.SetActors(who);
                f.SetHole(Worlds.At(lay, 0, 2), 1.0f);
                st.Footing(s, who);   // 장이 흐르기 전: 구멍 곁이 아니니 서 있다
                bool before = st.RagdollOf(0) < 0;
                s.Step();
                Check("몸 물체 (라) 검토 고침: 외판 1m² 구멍에서 두 칸 · 젖은 바닥에 선 이는 장이 흐른 뒤 이 틱 돌풍에 발이 떨어지고 — 이번 틱 걷는 원판에서 빠진다 (제 몸 물체와 닿지 않게 — 예전: 한 틱에 제 몸과 27번 닿았다)",
                    before && st.RagdollOf(0) >= 0 && st.PeopleKnocked == 1 && st.ActorCount == 0,
                    $"흐르기 전 서 있음 {before} · 흐른 뒤 몸 물체 {st.RagdollOf(0)} · 발이 떨어짐 {st.PeopleKnocked} · 남은 원판 {st.ActorCount}");
            }
            // (나) 외판 1m² 구멍 곁의 이는 빨려 나가 10틱 (24초) 뒤 배에서 5m 넘게 멀어진다 · 찍고 되살린 세계와 같다 / 곁의 로봇도 미끄러져 나간다
            {
                var lay = Worlds.Ascii(SpaceRoom);
                (Sim s, int who, int bot) Make(bool robot)
                {
                    var s = Worlds.Full(lay, 1);
                    var m = s.Get<Mind>();
                    int who = robot ? -1 : m.AddPerson(s, Worlds.At(lay, 1, 2)), bot = robot ? m.AddRobot(s, Worlds.At(lay, 1, 2)) : -1;
                    s.Run(2);
                    s.Get<Structure>().DamageCause[Worlds.At(lay, 0, 2)] = s.Causes.Add(CauseKind.Setup, "외판에 파편이 박혔다", s.Tick);
                    s.Get<Field>().SetHole(Worlds.At(lay, 0, 2), 1.0f);
                    return (s, who, bot);
                }
                double Away(Sim s, int tag)
                {
                    var st = s.Get<Structure>();
                    int i = st.RagdollOf(tag);
                    if (i < 0 || !Structure.Flying(st.Props[i])) return 0;
                    var (lx, ly) = st.LocalOf(0, st.Props[i].X, st.Props[i].Y);
                    return Math.Sqrt((lx - 0.5) * (lx - 0.5) + (ly - 2.5) * (ly - 2.5));
                }
                var (s1, who1, _) = Make(false);
                var m1 = s1.Get<Mind>();
                s1.Run(2);
                var r = new Sim(s1.Setup); r.Load(s1.Save());
                s1.Run(8); r.Run(8);
                double d = Away(s1, m1[who1].C.Body);
                bool same = s1.Save().AsSpan().SequenceEqual(r.Save());
                var (s2, _, bot) = Make(true);
                var m2 = s2.Get<Mind>();
                s2.Run(3);
                double db = Away(s2, -1 - m2[bot].C.Mech);
                int flung = s1.Facts.Since(0).Count(x => x.Kind == FactKind.ObjectFlung);
                Check("몸 물체 (나): 외판 1m² 구멍 곁의 이는 빨려 나가 10틱 (24초) 뒤 배에서 5m 넘게 멀어진다 (떠감) · 나는 동안 찍고 되살린 세계와 같다 · '물건이 날아갔다'로 세지 않는다 (검토 고침) / 곁의 로봇도 바람에 밀려 구멍 밖으로",
                    d > 5 && m1[who1].C.Adrift != 0 && same && flung == 0 && s2.Get<Structure>().RobotsKnocked >= 1 && db > 1,
                    $"사람 {d:0.0}m · 떠감 {m1[who1].C.Adrift} · 되살림 같음 {same} · 물건 날아감 사실 {flung} / 로봇 밀림 {s2.Get<Structure>().RobotsKnocked} · {db:0.0}m");
            }
        }

        // ── PH15 판 조각: 떨어져 나온 잔칸은 강철 판 물체 — 튀고 (세게 맞으면 금 · 구멍) · 트인 칸에 들면 실려 미끄러지다 선다 · 돌아 있는 배에도 운석은 제 자세대로 맞는다 ──
        if (Do("판 조각"))
        {
            Console.WriteLine("\n[판 조각 — 튀고 · 쌓이고 · 미끄러진다 (PH15)]");
            // (가) 40kg 판 조각이 60m/s로 외판에 맞으면 튀어 나가고 (반발 0.3 — 되돌아 나는 쪽으로) · 72kJ로 외판이 상한다 (원인 '판 조각')
            {
                var s = PropWorld(1);
                var st = s.Get<Structure>(); var L = s.Layout;
                st.EnsureBody();
                s.Step();
                int id = st.ThrowPlate(s, 6.5, -4, 0, 60, 40, s.Causes.Add(CauseKind.Setup, "시험 — 이웃 배에서 떨어진 판 조각이 날아왔다", s.Tick));
                s.Run(2);
                int i = st.IndexOf(id);
                var p = i >= 0 ? st.Props[i] : default;
                int roof = L.Index(6, 1);
                string story = st.DamageCause[roof] != 0 ? s.Causes.Story(st.DamageCause[roof]) : "";
                Check("판 조각 (가): 40kg 판 조각이 60m/s로 외판에 맞으면 되튀어 나가고 (반발 0.3 — 붙어 사라지지 않는다) · 72kJ에 외판이 상한다 (원인 사슬에 '판 조각')",
                    i >= 0 && Structure.Flying(p) && p.Vy < -5 && p.Vy > -25 && st.PlateHits == 1 && story.Contains("판 조각"),
                    $"판 조각 {(i >= 0 ? (Structure.Flying(p) ? "남 · 난다" : "남") : "없음")} · 빠르기 {p.Vy:0.0}m/s · 세게 맞음 {st.PlateHits} · 외판 \"{story}\"");
            }
            // (나) 외판 큰 구멍으로 5m/s로 들어온 판 조각은 방 바닥에 실려 미끄러지다 선다 (쌓인다 — 날아 지나가지 않는다)
            {
                var s = PropWorld(1);
                var st = s.Get<Structure>(); var f = s.Get<Field>(); var L = s.Layout;
                st.EnsureBody();
                f.SetHole(L.Index(1, 4), 2.5f);
                s.Step();
                int id = st.ThrowPlate(s, -3, 4.5, 5, 0, 30, 0);
                s.Run(4);
                int i = st.IndexOf(id);
                var p = i >= 0 ? st.Props[i] : default;
                int c = i >= 0 ? st.CellOf(p) : -1;
                Check("판 조각 (나): 외판 큰 구멍으로 5m/s로 들어온 30kg 판 조각은 방 바닥에 실려 (배의 몸) 미끄러지다 선다",
                    i >= 0 && !Structure.Flying(p) && p.Host == 0 && p.State == 0 && c >= 0 && L.Kind[c] == CellKind.Floor && p.X > 2.5,
                    $"판 조각 {(i >= 0 ? $"({p.X:0.00},{p.Y:0.00}) 상태 {p.State} · 몸 {p.Host}" : "없음")} · 칸 {(c >= 0 ? L.Kind[c].ToString() : "-")}");
            }
            // (다) 배가 90° 돌아 있으면 세상 틀 +y로 오는 운석은 도면 왼쪽 외판을 맞는다 (예전: 도면 틀 그대로 — 윗 외판)
            {
                var lay = Worlds.Ascii(Dumbbell, Worlds.MindLegend);
                var s = Worlds.Full(lay, 1);
                var st = s.Get<Structure>(); var o = s.Get<Outside>();
                st.EnsureBody();
                var b = st.Bodies[0]; b.Th = Math.PI / 2; st.Bodies[0] = b;
                o.AddRock(0.005, 7000, 0, 1, 0, 40, s.Causes.Add(CauseKind.Setup, "시험 운석", s.Tick));
                int hit = -1;
                for (int k = 0; k < 4 && hit < 0; k++) { s.Step(); if (o.Struck.Count > 0) hit = o.Struck[0].Cell; }
                bool left = hit >= 0 && (lay.X(hit) == 0 || lay.Kind[lay.Index(lay.X(hit) - 1, lay.Y(hit))] == CellKind.Outside);
                bool top = hit >= 0 && (lay.Y(hit) == 0 || lay.Kind[lay.Index(lay.X(hit), lay.Y(hit) - 1)] == CellKind.Outside);
                Check("판 조각 (다) 운석과 배의 자세: 배가 90° 돌아 있으면 세상 틀 +y로 오는 운석은 도면 왼쪽 외판 (왼쪽이 우주인 칸)을 맞는다",
                    hit >= 0 && left && !top, hit >= 0 ? $"맞은 칸 ({lay.X(hit)},{lay.Y(hit)}) · 왼쪽이 우주 {left} · 위가 우주 {top}" : "맞음 없음");
            }
            // (라) 검토 고침: 같은 줄로 0.6m 사이를 두고 들어온 30kg 판 조각 둘은 서로 부딪혀 포개지지 않는다 (예전: 판 조각은 물체끼리 닿지 않아 한 자리에 겹쳤다)
            {
                var s = PropWorld(1);
                var st = s.Get<Structure>(); var f = s.Get<Field>(); var L = s.Layout;
                st.EnsureBody();
                f.SetHole(L.Index(1, 4), 2.5f);
                s.Step();
                int a = st.ThrowPlate(s, -3, 4.5, 6, 0, 30, 0), b = st.ThrowPlate(s, -3.6, 4.5, 6, 0, 30, 0);
                s.Run(6);
                int ia = st.IndexOf(a), ib = st.IndexOf(b);
                var pa = ia >= 0 ? st.Props[ia] : default; var pb = ib >= 0 ? st.Props[ib] : default;
                double d = Math.Sqrt((pa.X - pb.X) * (pa.X - pb.X) + (pa.Y - pb.Y) * (pa.Y - pb.Y));
                Check("판 조각 (라) 검토 고침: 같은 줄로 0.6m 사이를 두고 큰 구멍에 든 30kg 판 조각 둘은 서로 부딪혀 한 자리에 포개지지 않는다 (가운데 사이 0.2m 넘게 · 둘 다 배 안 바닥에)",
                    ia >= 0 && ib >= 0 && pa.Host == 0 && pb.Host == 0 && d > 0.2,
                    $"판 1 ({pa.X:0.00},{pa.Y:0.00}) 몸 {pa.Host} · 판 2 ({pb.X:0.00},{pb.Y:0.00}) 몸 {pb.Host} · 사이 {d:0.00}m");
            }
            // (마) 검토 고침: 우주를 나는 13kg 식량 상자가 8m/s로 외판에 맞아도 (416J) ④ 충격 길 (금 · 구멍 · 쾅)은 판 조각만 — 상자는 튈 뿐 (예전: 상자도 충격 · '판 조각 맞음'으로 셌다)
            {
                var s = PropWorld(1);
                var st = s.Get<Structure>(); var L = s.Layout;
                st.EnsureBody();
                int id = Slide(s, "식량 상자", 6.5, 3.5, 0, 0);
                s.Step();
                int i = st.IndexOf(id); var p = st.Props[i];
                p.State = 5; p.Host = -1; p.X = 6.5; p.Y = -4; p.Vx = 0; p.Vy = 8; p.Asleep = 0; st.Props[i] = p;
                long imp0 = st.Impacts, b0 = st.Bounces;
                s.Run(2);
                int roof = L.Index(6, 1);
                Check("판 조각 (마) 검토 고침: 우주를 나는 13kg 식량 상자가 8m/s로 외판에 맞으면 튀기만 한다 — 충격 · 판 조각 맞음으로 세지 않는다 · 외판 원인이 비어 있다",
                    st.Impacts == imp0 && st.PlateHits == 0 && st.DamageCause[roof] == 0 && st.Bounces > b0,
                    $"충격 +{st.Impacts - imp0} · 판 조각 맞음 {st.PlateHits} · 튐 +{st.Bounces - b0} · 외판 원인 {(st.DamageCause[roof] != 0 ? s.Causes.Story(st.DamageCause[roof]) : "-")}");
            }
        }
}
}
