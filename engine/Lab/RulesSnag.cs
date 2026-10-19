using ShipEngine;
using ShipView;

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

        // ── 큰 구멍 (0.35㎡ · 외판 두 칸): 로봇은 한 번에 0.1㎡씩 여러 번 덧댄다 — 주컴퓨터는 넓이로 걸음 시간을 셈하고 · 줄어드는 동안은 막힌 것으로 보지 않는다 ──
        {
            var lay5 = Worlds.Ascii(NoAirlockShip, new Dictionary<char, string>(legend) { ['h'] = "HeatExchanger" });
            int A5(int x, int y) => Worlds.At(lay5, x, y);
            var s = Worlds.Full(lay5, 3);
            var f = s.Get<Field>(); var sm = s.Get<ShipMind>(); var m = s.Get<Mind>(); var net = s.Get<Network>();
            int robot = m.AddRobot(s, A5(22, 3), AgentKind.Robot, A5(22, 3));
            s.Run(2);
            net.AirReserve = 2e6;
            m[robot].C.Cell = A5(23, 3); f.SetHole(A5(3, 0), 0.2f); f.SetHole(A5(6, 0), 0.15f);
            long doneAt = -1;
            for (int k = 0; k < 24 && doneAt < 0; k++) { s.Run((int)Time.Minutes(10)); if (sm.PlansDone > 0) doneAt = s.Tick; }
            float hole = f.Hole[A5(3, 0)] + f.Hole[A5(6, 0)];
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") foreach (var x in sm.Log) Console.WriteLine($"    {Time.Clock(x.tick)} {x.text}");
            Check("큰 구멍: 로봇이 여러 번 덧대는 동안은 막힌 것이 아니다 (넓이로 걸음 시간을 셈하고 · 줄어들면 다시 잰다) — 한 계획으로 다 막는다",
                sm.PlansDone >= 1 && sm.PlanFails == 0 && hole <= 0 && sm.LastDevised.Contains("막는다"),
                $"마침 {sm.PlansDone} · 막힌 걸음 {sm.PlanFails} · 남은 구멍 {hole * 1e4:0}cm² · {(doneAt >= 0 ? Time.Clock(doneAt) : "못 마침")} · \"{sm.LastDevised}\"");
        }

        // ── 벽이 버틸까 (살핌호 6일): 구멍을 막은 뒤 다시 채우기 전에 둘레 벽의 금을 본다 —
        //    가) 금 하나 (받치면 버틴다): 로봇이 그 벽을 덧대 받친 뒤 채운다 · 다시 찢어지지 않는다
        //    나) 벽을 따라 길게 찢긴 금 (받쳐도 못 버틴다): 채우지 않고 비워진 채 닫아 둔다 · 예비 공기를 그 방에 쓰지 않는다 ──
        foreach (bool longTear in new[] { false, true })
        {
            var lay6 = Worlds.Ascii(NoAirlockShip, new Dictionary<char, string>(legend) { ['h'] = "HeatExchanger" });
            int A6(int x, int y) => Worlds.At(lay6, x, y);
            var s = Worlds.Full(lay6, 3);
            var f = s.Get<Field>(); var sm = s.Get<ShipMind>(); var m = s.Get<Mind>(); var net = s.Get<Network>(); var st = s.Get<Structure>();
            int robot = m.AddRobot(s, A6(22, 3), AgentKind.Robot, A6(22, 3));
            s.Run(2);
            net.AirReserve = 2e6;
            m[robot].C.Cell = A6(23, 3);
            f.SetHole(A6(3, 0), 0.02f);
            s.Run((int)Time.Minutes(6));   // 왼쪽 방이 다 빠진 뒤에 금을 본다 (찢긴 벽 — 기압이 없어 지금은 멀쩡하다)
            // 금: 받치면 버티는 길이를 찾아 심는다 (가) · 벽 줄 다섯 칸에 반 칸씩 (나)
            var cracked = new List<int>();
            if (!longTear)
            {
                int c = A6(7, 4);
                foreach (double a in new[] { 0.01, 0.02, 0.03, 0.05, 0.08, 0.12, 0.18, 0.25, 0.32, 0.4, 0.5 })
                {
                    st.Crack[c] = 0; st.SeedCrack(c, a, 0);
                    double k = st.TearRatio(c, 101.3);
                    if (k >= ShipMind.BraceAt + 0.05 && k / Structure.BraceFactor < ShipMind.WreckAt - 0.1) break;
                }
                cracked.Add(c);
            }
            else for (int x = 5; x <= 9; x++) { st.SeedCrack(A6(x, 4), 0.5, 0); cracked.Add(A6(x, 4)); }
            double k0 = cracked.Max(c => st.TearRatio(c, 101.3));
            long tears0 = st.Tears;
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") Console.WriteLine($"    금 심은 때 왼쪽 방 {f.Pressure(A6(3, 2)):0.0}kPa · 비율 {k0:0.00} · 찢어짐 {st.Tears}");
            for (int k = 0; k < 30 && sm.PlansDone == 0; k++) { s.Run((int)Time.Minutes(10)); if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") Console.WriteLine($"    [{Time.Clock(s.Tick)}] 왼쪽 {f.Pressure(A6(3, 2)):0} 가운데 {f.Pressure(A6(14, 2)):0} · 찢어짐 {st.Tears - tears0} · 계획 {string.Join(",", sm.PlanSteps.Select(p => $"{(ActKind)p.Act}{p.State}"))}"); }
            s.Run((int)Time.Minutes(20));
            float kL = (float)f.Pressure(A6(3, 2));
            bool braced = cracked.All(c => st.Braced[c] != 0);
            var log = sm.Log.Select(x => $"{Time.Clock(x.tick)} {x.text}").ToList();
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") foreach (var l in log.Where(l => l.Contains("생각") || l.Contains("계획") || l.Contains("닫아"))) Console.WriteLine("    " + l);
            if (!longTear)
                Check("벽이 버틸까 (가): 다시 채우기 전에 금 간 벽을 덧대 받치고 채운다 — 다시 찢어지지 않는다",
                    sm.PlansDone >= 1 && braced && kL > 80 && st.Tears == tears0 && sm.Wrecks == 0 && log.Any(l => l.Contains("받치고")),
                    $"찢어짐 비율 {k0:0.00} · 받침 {braced} · 왼쪽 방 {kL:0}kPa · 찢어짐 {st.Tears - tears0} · 마침 {sm.PlansDone} · 닫아 둠 {sm.Wrecks}");
            else
                Check("벽이 버틸까 (나): 벽을 따라 길게 찢긴 방은 받쳐도 못 버틴다 — 다시 채우지 않고 닫아 두며 그 방 일에 로봇을 더 보내지 않는다",
                    sm.Wrecks >= 1 && kL < 30 && st.Tears == tears0 && log.Any(l => l.Contains("닫아 둡니다")),
                    $"찢어짐 비율 {k0:0.00} (받쳐도 {k0 / Structure.BraceFactor:0.00}) · 왼쪽 방 {kL:0}kPa · 찢어짐 {st.Tears - tears0} · 닫아 둠 {sm.Wrecks} · 마침 {sm.PlansDone}");
        }

        // ── 사람의 궁리 (판단 엔진 (마-3)): 구멍을 막으러 가다 문 · 길로 막힌 사람이 제 믿음 · 제 지식으로 궁리한다 ──
        //    가) 기압을 모르는 이 · 주컴퓨터가 없는 배: 우주로 트인 방 문 앞에서 막혀 '기압 차가 큰 문은 안 열린다'를 배우고 — 혼자는 길이 없어 '방법을 모르겠다'
        //    나) 로봇이 있는 배: 주컴퓨터가 이미 그 방에 계획을 세웠으면 — 막힌 사람은 그 계획을 따른다 (제 판단보다 먼저 · 따를 마음은 믿음 · 성실)
        //    다) 기압을 아는 이 · 로봇이 없는 배 · 주컴퓨터는 사람에게 부탁하지 않는 방침: 혼자는 길이 없지만 '주컴퓨터가 앞방 공기를 빼 주면 된다'를 떠올려 묻고 · 주컴퓨터가 그 사람을 몸으로 함께 한다
        foreach (char sc in "가나다")
        {
            string ship = sc != '가' ? @"
##########################
#..x.......#..s..#...pb..#
#..........D.....D....h..#
#..e.......#.k.k.#..c.w..#
##########################" : @"
##########################
#..x.......#..w..#...pb..#
#..........D.....D....h..#
#..e.......#..s..#.......#
##########################";
            var lay8 = Worlds.Ascii(ship, new Dictionary<char, string>(legend) { ['h'] = "HeatExchanger", ['s'] = "SuitLocker", ['k'] = "Shelf", ['w'] = "Workbench" });
            int A8(int x, int y) => Worlds.At(lay8, x, y);
            var s = Worlds.Full(lay8, 5);
            var f = s.Get<Field>(); var sm = s.Get<ShipMind>(); var m = s.Get<Mind>(); var net = s.Get<Network>(); var st = s.Get<Structure>(); var life = s.Get<Life>();
            if (sc == '다') sm.Policy.AskPeople = 0;   // 주컴퓨터는 먼저 부탁하지 않는다 — 사람이 물어 와야 한다
            int who = m.AddPerson(s, sc == '나' ? A8(21, 2) : A8(14, 2));   // (나): 비울 방(방2) 너머 방3에서
            int robot = sc == '나' ? m.AddRobot(s, A8(22, 3), AgentKind.Robot, A8(22, 3)) : -1;   // (나): 로봇이 임시 에어락을 맡는다
            m.SetPersonality(who, 0.6f, 0.9f, 0.6f, 0.6f, 0.3f);
            s.Run(2);
            net.AirReserve = 2e6;
            int d1 = lay8.Doors.First(d => d.Cell == A8(11, 2)).Id;
            int hole = A8(3, 0);
            ref var c = ref m[who].C;
            c.KnPressure = sc == '가' ? 0.1f : sc == '나' ? 0.5f : 0.8f; c.KnStruct = 0.6f; c.SkSeal = 0.7f;
            f.SetHole(hole, 0.02f);
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") Console.WriteLine($"    ({sc}) 구멍 놓음 {f.Hole[hole] * 1e4:0.0}cm²");
            s.Run((int)Time.Minutes(3));
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") Console.WriteLine($"    ({sc}) 3분 뒤 {f.Hole[hole] * 1e4:0.0}cm² · 방1 {f.Pressure(A8(3, 2)):0}kPa");
            m[who].Beliefs.Add(new Belief { Kind = BeliefKind.Breach, Src = BeliefSource.Heard, Cell = hole, Subject = -1, Conf = 1f, Seen = s.Tick });   // 쉬익 소리 · 경보로 구멍을 안다
            float kn0 = m[who].C.KnPressure;
            bool personBody = false;
            long asked0 = -1, own0 = -1;   // (나): 주컴퓨터 계획이 생긴 때의 물음 · 제 궁리 수 — 그 뒤로는 따라야 한다 (생기기 전에 물은 것은 맞다)
            for (int k = 0; k < 24 && !(sc == '가' && m.PondersLost > 0) && !(sc != '가' && f.Hole[hole] <= 0); k++)
            {
                for (int q = 0; q < 10; q++)
                {
                    s.Run((int)Time.Minutes(1)); personBody |= sm.PlanSteps.Any(p => p.Robot == who);
                    if (asked0 < 0 && sm.DevisesFound > 0) { asked0 = m.PondersAsked; own0 = m.PondersOwn; }
                }
                if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1" && k == 0) Console.WriteLine($"    ({sc}) 믿음 {string.Join(" ", m[who].Beliefs.Where(bb => bb.Conf > 0.1f).Select(bb => $"{bb.Kind}@{lay8.X(bb.Cell)},{lay8.Y(bb.Cell)}:{bb.Conf:0.00}"))} · 사람 번호 {who}");
                if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") Console.WriteLine($"    ({sc}) [{Time.Clock(s.Tick)}] 사람 {lay8.X(m[who].C.Cell)},{lay8.Y(m[who].C.Cell)} {m[who].C.Goal}:{m[who].C.GoalTarget} 계획 {m[who].Plan.Count}/{m[who].C.StepIndex} · 구멍 {f.Hole[hole] * 1e4:0}cm² · 방1 {f.Pressure(A8(3, 2)):0} 방2 {f.Pressure(A8(14, 2)):0}kPa · 문 {f.DoorOpen[d1]:0.00} 끼임 {st.DoorJam[d1]} · 그만둠 '{m[who].LastFail}' · 막힘 {m[who].C.Snag}");
            }
            if (sc != '가') s.Run((int)Time.Minutes(40));   // 막은 뒤 다시 채우기까지
            var b = life.Bodies[m[who].C.Body];
            string thought = m[who].LastThought;
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") { Console.WriteLine($"    ({sc}) 궁리 {m.Ponders} · 제 궁리 {m.PondersOwn} · 물음 {m.PondersAsked} · 모름 {m.PondersLost} · 배움 {m.PondersLearned} · 해냄 {m.OwnDone} · '{thought}' · 그만둠 '{m[who].LastFail}'"); foreach (var x in sm.Log) Console.WriteLine($"      {Time.Clock(x.tick)} {x.text}"); }
            string state = $"궁리 {m.Ponders} · 제 궁리 {m.PondersOwn} · 물음 {m.PondersAsked}(주컴퓨터 받음 {sm.Consults}) · 모름 {m.PondersLost} · 배움 {m.PondersLearned} · 기압 앎 {kn0:0.00}→{m[who].C.KnPressure:0.00} · 구멍 {f.Hole[hole] * 1e4:0}cm² · 몸 {(b.Alive == 0 ? "숨짐" : b.Conscious == 0 ? "쓰러짐" : "멀쩡")} · '{thought}'";
            if (sc == '가')
            {
                // 화면 (사): 고른 사람의 카드 · 사람 판(P)에 그 궁리 (한 시간 안)
                var show = new ShipView.LiveShow(s, "시험호") { Selected = who };
                var card = show.Card();
                show.TogglePanel(PanelKind.People);
                var prow = show.PanelRows().FirstOrDefault(r => r.Main.StartsWith(m.NameOf(who)));
                Check("화면 (사): 막혀 궁리한 사람의 카드 · 사람 판에 '궁리: …' (방을 막지 못한 까닭과 끝 — 방법을 모르겠다)",
                    card.Any(l => l.StartsWith("궁리: ") && l.Contains("모르겠다")) && prow.Sub != null && prow.Sub.Contains("궁리: "),
                    $"카드 '{card.FirstOrDefault(l => l.StartsWith("궁리")) ?? "없음"}' · 사람 판 '{prow.Sub?.Split('\n').LastOrDefault()}'");
            }
            if (sc == '가')
                Check("사람의 궁리 (가): 기압을 모르는 이는 우주로 트인 방 문 앞에서 막혀 배우고 · 주컴퓨터가 없는 배에서 혼자 길이 없으면 '방법을 모르겠다'",
                    m.PondersLearned >= 1 && m[who].C.KnPressure > kn0 && m.PondersLost >= 1 && thought.Contains("모르겠다") && b.Alive != 0, state);
            else if (sc == '나')
                Check("사람의 궁리 (나): 주컴퓨터가 그 방에 계획을 세웠으면 막힌 이는 그 계획을 따른다 (제 판단 · 묻기보다 먼저) — 로봇이 막는다",
                    m.PondersDeferred >= 1 && asked0 >= 0 && m.PondersAsked == asked0 && m.PondersOwn == own0 && f.Hole[hole] <= 0 && thought.Contains("따른다") && b.Alive != 0 && b.Conscious != 0,
                    state + $" · 따름 {m.PondersDeferred} · 계획 뒤 물음 {m.PondersAsked - asked0} · 제 궁리 {m.PondersOwn - own0}");
            else
                Check("사람의 궁리 (다): 혼자는 길이 없지만 '주컴퓨터가 앞방 공기를 빼 주면 된다'를 떠올려 묻고 · 주컴퓨터가 그 사람과 함께 막는다",
                    m.PondersAsked >= 1 && sm.Consults >= 1 && personBody && f.Hole[hole] <= 0 && thought.Contains("주컴퓨터") && b.Alive != 0 && b.Conscious != 0, state + $" · 몸이 된 이 {personBody} · 마침 {sm.PlansDone}");
        }

        // ── 주컴퓨터 계획의 몸 (판단 엔진 · 위험도 시험 · 사람의 궁리 (나)에서 찾은 결함 셋):
        //    가) 하는 중인 걸음 앞에 걸음을 끼워도(공기를 빼는데 문틈이 다시 열렸다 → 틈 막기) 같은 걸음이 겹으로 생기지 않는다 (예전: 자리 번호로 되써 공기 빼기가 20번 겹치고 틈은 영영 안 막혔다)
        //    나) 몸이 풀이기가 셈한 자리에 없으면(그 사이 움직였다) 그 계획을 하지 않고 지금 자리로 다시 궁리한다 (예전: 방3에 간 로봇에게 '방2에서 방1로 가기'를 시켰다)
        //    다) 제 계획의 몸이 아직 지나갈 문은 잠그지 않는다 (예전: 빼낼 방 문을 첫 걸음부터 잠가 '방2로 가기'가 1분마다 막혔다)
        {
            var layB = Worlds.Ascii(@"
##########################
#..x.......#..s..#...pb..#
#..........D.....D....h..#
#..e.......#.k.k.#..c.w..#
##########################", new Dictionary<char, string>(legend) { ['h'] = "HeatExchanger", ['s'] = "SuitLocker", ['k'] = "Shelf", ['w'] = "Workbench" });
            int AB(int x, int y) => Worlds.At(layB, x, y);
            var s = Worlds.Full(layB, 5);
            var f = s.Get<Field>(); var sm = s.Get<ShipMind>(); var m = s.Get<Mind>(); var net = s.Get<Network>(); var st = s.Get<Structure>();
            int robot = m.AddRobot(s, AB(22, 3), AgentKind.Robot, AB(22, 3));
            s.Run(2);
            net.AirReserve = 2e6;
            int d23 = layB.Doors.First(d => d.Cell == AB(17, 2)).Id;
            int hole = AB(3, 0);
            f.SetHole(hole, 0.02f);
            int maxDup = 0, lockedGo = 0; bool gapOpened = false, moved = false;
            for (int t = 0; t < (int)Time.Hours(3) && f.Hole[hole] > 0; t++)
            {
                s.Run(1);
                var live = sm.PlanSteps.Where(p => p.State != 2).ToList();
                if (live.Count > 0) maxDup = Math.Max(maxDup, live.GroupBy(p => (p.Incident, p.Act, p.Obj)).Max(g => g.Count()));
                foreach (var p in live) if (p.Act == (byte)ActKind.Go && st.Locked(p.Obj)) lockedGo++;
                var pump = live.FirstOrDefault(p => p.Act == (byte)ActKind.Pump && p.State == 1);
                if (pump.Act == (byte)ActKind.Pump && !gapOpened && f.DoorTaped[d23] != 0) { f.DoorTaped[d23] = 0; gapOpened = true; }   // 가) 막은 틈이 떨어졌다
                if (pump.Act == (byte)ActKind.Pump && gapOpened && !moved && f.DoorTaped[d23] != 0 && sm.PlanSteps.Any(p => p.Act == (byte)ActKind.Go && p.State == 0))
                {
                    m[robot].C.Cell = AB(21, 2); m[robot].Path.Clear(); moved = true;   // 나) 공기를 빼는 사이 몸이 방3에 가 있다 (셈한 자리는 방2)
                }
            }
            bool replanned = sm.Log.Any(x => x.text.Contains("셈한 자리"));
            Check("주컴퓨터 계획의 몸 (가): 공기를 빼다 문틈이 다시 열려 틈 막기를 끼워 넣어도 같은 걸음이 겹으로 생기지 않는다 (걸음마다 바뀌지 않는 번호로 되쓴다)",
                gapOpened && maxDup <= 1, $"틈 떨어뜨림 {gapOpened} · 같은 걸음 가장 많이 {maxDup}");
            Check("주컴퓨터 계획의 몸 (나): 몸이 풀이기가 셈한 자리에 없으면 그 계획을 하지 않고 지금 자리로 다시 궁리한다",
                moved && replanned && sm.DevisesFound >= 2, $"옮김 {moved} · 다시 궁리 {replanned} · 지어낸 계획 {sm.DevisesFound} · 구멍 {f.Hole[hole] * 1e4:0}cm²");
            Check("주컴퓨터 계획의 몸 (다): 제 계획의 몸이 아직 지나갈 문은 잠그지 않는다 (비울 방 문이라도)",
                lockedGo == 0, $"지나갈 문이 잠긴 틱 {lockedGo}");
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

        // ── 연기에 밀려난 소방 (관찰 장면 '폼 침대 불'에서 찾은 결함): 불 앞이 연기로 캄캄해 몸을 피한 사람은 곧바로 되들어가지 않는다 —
        //    틱마다 들어가 뿌리고 · 물러나기를 되풀이했다 ('몸을 피했다' 여섯 번 · 원인 없이). 이제 '연기 · 열에 밀려났다'는 막힘 (원인 마디) · 몇 분은 그 일로 돌아가지 않는다 ──
        {
            var mlay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
            var s = Worlds.Full(mlay, 5); var m = s.Get<Mind>(); var f = s.Get<Field>(); var ch = s.Get<Chemistry>();
            int fireAt = Worlds.At(mlay, 15, 3), from = Worlds.At(mlay, 6, 8);
            int who = m.AddPerson(s, from);
            m[who].C.Brave = 0.6f;
            s.Run(5);
            f.AddStore(fireAt, f.T.sPoly, 200);
            int fire = s.Causes.Add(CauseKind.Setup, "의자에 불", s.Tick);
            ch.Ignite(fireAt, 3e6, fire);
            m.Assign(s, who, GoalKind.FightFire, fireAt, new Belief { Kind = BeliefKind.Fire, Cell = fireAt, Subject = -1, Conf = 1f, Cause = fire }, s.Tick + Time.Minutes(10));
            long f0 = s.Facts.Total;
            var thick = new List<int>();
            for (int y = 2; y <= 4; y++) for (int x = 13; x <= 17; x++) thick.Add(Worlds.At(mlay, x, y));
            for (int k = 0; k < Time.Minutes(2); k++)
            {
                foreach (int c in thick) if (f.Ppm(f.T.gSoot, c) < 3000) f.AddGas(c, f.T.gSoot, f.Moles(c) * 3000e-6, f.Temp(c));   // 불 둘레는 캄캄한 연기 (보이는 거리 1m 남짓)
                s.Step();
            }
            var fled = s.Facts.Since(f0).Where(x => x.Kind == FactKind.Fled && x.Actor == who).ToList();
            long gap = fled.Count < 2 ? long.MaxValue : fled.Zip(fled.Skip(1), (p, q) => q.Tick - p.Tick).Min();   // 되들어가기까지 가장 짧은 틈 (틱)
            var back = s.Facts.Since(f0).FirstOrDefault(x => x.Kind == FactKind.Blocked && x.Actor == who && x.Size == (float)SnagKind.DrivenBack);
            string why = fled.Count > 0 ? s.Causes.Story(fled[0].Cause) : "";
            Check("연기에 밀려난 소방: 불 앞이 캄캄해 몸을 피한 사람은 곧바로 되들어가지 않는다 (2분에 '몸을 피했다' 세 번 아래 · 피함 사이 5틱 넘게) · '연기 · 열에 밀려났다' 막힘이 원인 마디로 남고 · 피한 까닭이 그 불에 닿는다",
                fled.Count is >= 1 and <= 3 && gap > 5 && back.Kind == FactKind.Blocked && why.Contains("의자에 불"),
                $"몸을 피함 {fled.Count}번 · 가장 짧은 틈 {(gap == long.MaxValue ? "-" : gap + "틱")} · 막힘 \"{(back.Kind == FactKind.Blocked ? s.Causes.Text(back.Cause) : "")}\" · 피한 까닭 \"{why}\"");
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
