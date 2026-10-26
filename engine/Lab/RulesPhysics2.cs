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
    }
}
