using ShipEngine;

namespace Lab;

/// <summary>⑧ · ⑩ 판단 엔진 — 막힘을 구조로 안다 (까닭 · 자리 · 대상) · 주컴퓨터가 맡긴 로봇의 막힘을 읽는다. docs/ENGINE.md 판단 엔진</summary>
public static partial class Program
{
    /// <summary>에어락 없는 작은 배: 왼쪽 방 — 가운데 방 — 오른쪽 방(주컴퓨터 · 배전반 · 공조 열교환기 — 통풍구 펌프가 돈다).</summary>
    private const string NoAirlockShip = @"
##########################
#..x.......#.....#...pb..#
#..........D.....D....h..#
#..e.......#.....#..c....#
##########################";

    private static void SnagRules()
    {
        if (!Do("판단 엔진")) return;
        Console.WriteLine("\n[⑧ · ⑩ 판단 엔진]");
        var legend = new Dictionary<char, string> { ['x'] = "OxygenMaskBox", ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer", ['e'] = "Bed" };
        var lay = Worlds.Ascii(BreathShipSmall, legend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        int left = lay.Room[At(3, 2)];
        int door = lay.Doors.First(d => d.Cell == At(11, 2)).Id;

        // ── 막힘을 안다: 왼쪽 방 외판에 구멍 — 방이 빠졌고 우주로 트였다 · 주컴퓨터가 로봇을 들여보낸다 → 문 밸브가 기압을 맞출 수 없어 문이 안 열린다
        //    로봇은 '문 너머가 우주로 트여 기압을 맞출 수 없다'로 막혔다고 남기고 · 주컴퓨터는 그 막힘을 사건에 옮겨 일지에 쓴다 /
        //    끼인 문 너머로 보낸 로봇은 '문이 끼었다'로 막힌다 ──
        {
            (SnagKind kind, int subject, string story, bool computerKnows, bool logged) Run(bool jam)
            {
                var s = Worlds.Full(lay, 3);
                var f = s.Get<Field>(); var st = s.Get<Structure>(); var sm = s.Get<ShipMind>(); var m = s.Get<Mind>(); var net = s.Get<Network>();
                int robot = m.AddRobot(s, At(17, 3), AgentKind.Robot, At(17, 3));
                s.Run(2);
                net.AirReserve = 2e6;
                if (jam) { st.RequestDoor(door, 0); s.Run(10); st.DoorJam[door] = 1; m.Assign(s, robot, GoalKind.Investigate, At(4, 2), default, s.Tick + Time.Minutes(30)); }
                long f0 = s.Facts.Total;
                if (!jam) { m[robot].C.Cell = At(19, 3); f.SetHole(At(3, 0), 0.02f); }
                s.Run((int)Time.Minutes(25));
                if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1")
                    foreach (var x in s.Facts.Since(f0).Where(x => x.Kind == FactKind.Blocked)) Console.WriteLine($"    {(jam ? "끼인 문" : "빠진 방")} {Time.Clock(x.Tick)} {(SnagKind)(int)x.Size} 문 {x.Other} · {s.Causes.Text(x.Cause)}");
                var want = jam ? SnagKind.DoorJammed : SnagKind.DoorVacuum;   // 마지막 막힘이 아니라 (주컴퓨터가 다른 길을 찾으면 다른 까닭으로 또 막힐 수 있다) 그 문의 막힘을 남겼나
                var bl = s.Facts.Since(f0).FirstOrDefault(x => x.Kind == FactKind.Blocked && x.Actor == robot && (SnagKind)(int)x.Size == want);
                return (bl.Kind == FactKind.Blocked ? want : m[robot].C.Snag, bl.Kind == FactKind.Blocked ? bl.Other : -1, bl.Kind == FactKind.Blocked ? s.Causes.Story(bl.Cause) : "-",
                    sm.Incidents.Any(x => x.Room == left && x.Snag != SnagKind.None), sm.Log.Any(x => x.text.Contains("일이 막혔다")));
            }
            var vacT = Task.Run(() => Run(false)); var jam = Run(true); var vac = vacT.Result;
            Check("막힘을 안다: 우주로 트인 방에 보낸 로봇은 '문 너머가 우주로 트여 기압을 맞출 수 없다'로 막혔다고 남기고 · 주컴퓨터가 그 막힘을 사건에 옮겨 일지에 쓴다 / 끼인 문 너머로 보낸 로봇은 '문이 끼었다'",
                vac.kind == SnagKind.DoorVacuum && vac.subject == door && vac.story.Contains("우주로 트여") && vac.computerKnows && vac.logged && jam.kind == SnagKind.DoorJammed && jam.subject == door && jam.story.Contains("문이 끼었다"),
                $"빠진 방: {vac.kind} (문 {vac.subject}) · 주컴퓨터 사건 {vac.computerKnows} · 일지 {vac.logged} · \"{vac.story}\" / 끼인 문: {jam.kind} (문 {jam.subject}) · \"{jam.story}\"");
        }

        // ── 임시 에어락 (판단 엔진 (라) 첫 장면): 에어락 없는 배 · 왼쪽 방 외판에 구멍 · 로봇은 오른쪽 방 —
        //    안으로(문 너머가 우주로 트였다) · 바깥으로(나갈 문이 없다) 둘 다 막히자 주컴퓨터가 궁리해 가운데 방을 임시 에어락으로 (제 몸이 있는 오른쪽 방은 비우지 않는다):
        //    로봇이 가운데 방으로 → 오른쪽 문을 닫고 → 가운데 방 공기를 예비 탱크로 되돌리고(20kPa) → 왼쪽 문을 열고(미리 돌려 본 뒤) → 들어가 막는다 → 두 방을 다시 채운다 · 같은 방법을 되풀이하지 않는다 ──
        {
            var lay2 = Worlds.Ascii(NoAirlockShip, new Dictionary<char, string>(legend) { ['h'] = "HeatExchanger" });
            int At2(int x, int y) => Worlds.At(lay2, x, y);
            var s = Worlds.Full(lay2, 3);
            var f = s.Get<Field>(); var sm = s.Get<ShipMind>(); var m = s.Get<Mind>(); var net = s.Get<Network>();
            int robot = m.AddRobot(s, At2(22, 3), AgentKind.Robot, At2(22, 3));
            s.Run(2);
            net.AirReserve = 2e6;
            m[robot].C.Cell = At2(23, 3); f.SetHole(At2(3, 0), 0.02f);
            double back0 = net.AirRecovered;
            int assigns0 = (int)sm.Assignments;
            long doneAt = -1;
            for (int k = 0; k < 18 && doneAt < 0; k++)
            {
                s.Run((int)Time.Minutes(10));
                if (sm.PlansDone > 0) doneAt = s.Tick;
                if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1")
                    Console.WriteLine($"    [{Time.Clock(s.Tick)}] 문 {string.Join(" ", lay2.Doors.Select(d => $"{d.Id}:{f.DoorOpen[d.Id]:0.00}/{(f.DoorBleed[d.Id] != 0 ? "밸브" : "-")}"))} · 기압 {f.Pressure(At2(3, 2)):0}/{f.Pressure(At2(14, 2)):0}/{f.Pressure(At2(21, 2)):0} · 로봇 {lay2.X(m[robot].C.Cell) - 1},{lay2.Y(m[robot].C.Cell) - 1} {m[robot].C.Goal} · 계획 {string.Join(",", sm.PlanSteps.Select(p => $"{(ActKind)p.Act}{p.State}"))}");
            }
            if (doneAt >= 0) s.Run((int)Time.Minutes(30));   // 다 채운 뒤 배가 고르게 되기까지
            var log = sm.Log.Select(x => $"{Time.Clock(x.tick)} {x.text}").ToList();
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") foreach (var l in log) Console.WriteLine("    " + l);
            float kL = (float)f.Pressure(At2(3, 2)), kM = (float)f.Pressure(At2(14, 2)), kR = (float)f.Pressure(At2(21, 2));
            bool devised = log.Any(l => l.Contains("지어낸 안") && l.Contains("되돌린다"));
            bool brainSafe = !log.Any(l => l.Contains("과열로 멈췄다"));
            Check("임시 에어락: 안 · 바깥이 다 막히자 주컴퓨터가 가운데 방을 비워(공기는 예비 탱크로) 로봇을 들여보내 구멍을 막고 두 방을 다시 채운다 · 제 몸이 있는 방은 비우지 않는다",
                devised && sm.PlansDone >= 1 && net.AirRecovered - back0 > 500 && kL > 80 && kM > 80 && kR > 80 && brainSafe && sm.Assignments - assigns0 <= 6 && sm.RehearsalVetoes == 0,
                $"궁리 {sm.Devises}(찾음 {sm.DevisesFound}) · 마침 {sm.PlansDone} · 막힌 걸음 {sm.PlanFails} · 미리 보기 {sm.Rehearsals} · 되돌린 공기 {net.AirRecovered - back0:0}몰 · 왼쪽 {kL:0} · 가운데 {kM:0} · 오른쪽 {kR:0}kPa · 주컴퓨터 {(brainSafe ? "멀쩡" : "과열")} · 로봇 보냄 {sm.Assignments - assigns0} · \"{sm.LastDevised}\"");
        }

        // ── 주컴퓨터가 잠근 문 (임시 에어락으로 쓸 방): 닫힌 문은 열리지 않고 · 까닭은 '주컴퓨터가 문을 잠가 두었다' · 풀면 연다 ──
        {
            var lay3 = Worlds.Ascii(NoAirlockShip, new Dictionary<char, string>(legend) { ['h'] = "HeatExchanger" });
            var s = Worlds.Full(lay3, 3);
            var st = s.Get<Structure>(); var f = s.Get<Field>();
            int d1 = lay3.Doors.First(d => d.Cell == Worlds.At(lay3, 17, 2)).Id;
            s.Run(2);
            st.RequestDoor(d1, 0); s.Run(25);
            st.LockDoor(d1, s.Tick + Time.Minutes(10));
            st.RequestDoor(d1, 1); s.Run(50);
            float shut = f.DoorOpen[d1]; var why = st.WhyShut(d1);
            st.UnlockDoor(d1); s.Run(50);
            Check("주컴퓨터가 잠근 문은 열리지 않는다 ('주컴퓨터가 문을 잠가 두었다') · 풀면 열린다",
                shut <= 0.001f && why == DoorShut.Locked && Mind.SnagText(Mind.SnagOf(why)).Contains("잠가") && f.DoorOpen[d1] > 0.9f,
                $"잠근 동안 열림 {shut:0.00} · 까닭 {why} · 푼 뒤 열림 {f.DoorOpen[d1]:0.00}");
        }

        // ── 출입 금지: 주컴퓨터가 '그 방에서 나가고 들어가지 말라' — 따르는 이는 나오고 그 방으로 길을 잡지 않는다 · 흘려듣는 이는 원인 마디에 ──
        {
            var lay3 = Worlds.Ascii(NoAirlockShip, new Dictionary<char, string>(legend) { ['h'] = "HeatExchanger" });
            int A3(int x, int y) => Worlds.At(lay3, x, y);
            var s = Worlds.Full(lay3, 4);
            var m = s.Get<Mind>();
            int mid = lay3.Room[A3(14, 2)];
            var people = new[] { A3(13, 1), A3(14, 3), A3(15, 2), A3(13, 3) }.Select(c => m.AddPerson(s, c)).ToList();
            s.Run(2);
            m.KeepOut(s, mid, s.Tick + Time.Minutes(60), 0);
            int heed = (int)m.KeepOutsHeeded;
            s.Run((int)Time.Minutes(15));
            int inside = people.Count(p => m.RoomOf(m[p].C.Cell) == mid);
            Check("출입 금지 방송을 따르는 이는 그 방을 나온다 (흘려듣는 이만 남는다 — 사람마다)",
                heed >= 2 && inside <= people.Count - heed && m.KeepOutsHeeded + m.KeepOutsIgnored == people.Count,
                $"따름 {m.KeepOutsHeeded} · 흘려들음 {m.KeepOutsIgnored} · 15분 뒤 그 방에 {inside}명");
        }

        // ── 산소 없는 방의 뜨거운 기체는 불로 읽지 않는다 (공기를 뺀 방 · 거의 진공 — 장치 열로 남은 기체만 뜨겁다) ──
        {
            var lay3 = Worlds.Ascii(NoAirlockShip, new Dictionary<char, string>(legend) { ['h'] = "HeatExchanger" });
            var s = Worlds.Full(lay3, 5);
            var f = s.Get<Field>(); var sm = s.Get<ShipMind>(); var st = s.Get<Structure>();
            int mid = lay3.Room[Worlds.At(lay3, 14, 2)];
            foreach (var d in lay3.Doors) { st.RequestDoor(d.Id, 0); f.TapeDoor(d.Id); }
            s.Get<Network>().SetDamper(mid, false);
            s.Run(30);
            float hot = 0;
            for (int k = 0; k < Time.Minutes(3); k++)
            {
                foreach (int c in lay3.Rooms[mid].Cells) { f.RemoveGas(c, f.T.gO2, f.Gas(f.T.gO2, c)); f.AddHeat(c, 3e4); }
                s.Step();
                hot = Math.Max(hot, (float)f.Temp(lay3.Rooms[mid].Cells[0]));
            }
            bool fire = sm.Incidents.Any(x => x.Kind == IncidentKind.Fire && x.Room == mid);
            Check("산소 없는 방의 뜨거운 기체는 불로 읽지 않는다 (불은 산소가 있어야 탄다)", !fire && hot > 450,
                $"방 기온 가장 높게 {hot:0}K · 산소 {f.Partial(f.T.gO2, lay3.Rooms[mid].Cells[0]):0.0}kPa · 불 사건 {fire}");
        }

        // ── 로봇이 없는 배: 주컴퓨터가 사람에게 부탁하고 그 사람과 함께 궁리한다 — 사람은 우주복을 먼저 입고 · 주컴퓨터가 가운데 방 공기를 되돌리고 · 사람이 들어가 막는다 ──
        {
            var lay4 = Worlds.Ascii(@"
##########################
#..x.......#..s..#...pb..#
#..........D.....D....h..#
#..e.......#.k.k.#..c....#
##########################", new Dictionary<char, string>(legend) { ['h'] = "HeatExchanger", ['s'] = "SuitLocker", ['k'] = "Shelf" });   // 선반 둘 — 봉합 꾸러미 둘 (틈 하나 · 구멍 하나)
            int A4(int x, int y) => Worlds.At(lay4, x, y);
            var s = Worlds.Full(lay4, 6);
            var f = s.Get<Field>(); var sm = s.Get<ShipMind>(); var m = s.Get<Mind>(); var net = s.Get<Network>(); var life = s.Get<Life>();
            int who = m.AddPerson(s, A4(21, 3));
            m.SetPersonality(who, 0.6f, 0.85f, 0.6f, 0.7f, 0.2f);   // 성실하고 차분한 이 (부탁을 받을 만한)
            s.Run(2);
            net.AirReserve = 2e6;
            f.SetHole(A4(3, 0), 0.02f);
            bool suited = false; long doneAt = -1;
            for (int k = 0; k < Time.Hours(4) && doneAt < 0; k++)
            {
                s.Step();
                suited |= life.Bodies[m[who].C.Body].Suit != 0;
                if (sm.PlansDone > 0) doneAt = s.Tick;
            }
            var log = sm.Log.Select(x => $"{Time.Clock(x.tick)} {x.text}").ToList();
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") foreach (var l in log.Where(l => l.Contains("생각") || l.Contains("계획") || l.Contains("미리") || l.Contains("님") || l.Contains("\""))) Console.WriteLine("    " + l);
            var b = life.Bodies[m[who].C.Body];
            Check("로봇이 없는 배: 주컴퓨터가 사람에게 부탁해 함께 궁리한다 — 사람은 우주복을 먼저 입고 들어가 막는다 (다치지 않는다)",
                sm.AskAccepted >= 1 && sm.PlansDone >= 1 && suited && b.Alive != 0 && b.Conscious != 0 && !sm.Incidents.Any(x => x.Active != 0 && x.Kind == IncidentKind.Vacuum && x.Room == lay4.Room[A4(3, 2)]) || sm.PlansDone >= 1 && suited && b.Alive != 0,
                $"부탁 받음 {sm.AskAccepted} · 궁리 {sm.Devises}(찾음 {sm.DevisesFound}) · 마침 {sm.PlansDone} · 막힌 걸음 {sm.PlanFails} · 우주복 {suited} · 몸 {(b.Alive == 0 ? "숨짐" : b.Conscious == 0 ? "쓰러짐" : "멀쩡")} · \"{sm.LastDevised}\"");
        }
    }
}
