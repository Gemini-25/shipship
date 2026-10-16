using ShipEngine;

namespace Lab;

/// <summary>⑩ 주컴퓨터 규칙 시험 — 주컴퓨터가 있는 시험 배(Worlds.MindShipComputer).</summary>
public static partial class Program
{
    private static void ComputerRules()
    {
        if (!Do("주컴퓨터")) return;
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        Sim World(ulong seed = 1) => Worlds.Full(lay, seed);
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";

        // ── 아무도 없는 방의 불: 주컴퓨터가 감지 · 경보 · 다른 방 로봇을 보낸다 → 꺼진다 / 주컴퓨터가 멈춰 있으면 아무도 모른다 ──
        (bool robotOut, long alarms, long assigns, int burning) FireRun(bool computerOk)
        {
            var s = World();
            var m = s.Get<Mind>(); var ch = s.Get<Chemistry>(); var net = s.Get<Network>();
            int r = m.AddRobot(s, At(3, 7), AgentKind.Robot, At(3, 7));
            if (!computerOk) { int dev = net.Devices.First(d => d.Type == "MainComputer").Id; net.Age(dev, Part.Electric, 1.0); }
            s.Run(5);
            ch.Ignite(At(15, 1), 3e6, s.Causes.Add(CauseKind.Story, "빈 방 의자 밑에서 불씨가 튀었다", s.Tick));
            for (int k = 0; k < Time.Minutes(12) && s.Facts.CountOf(FactKind.Extinguished) == 0; k++)
            {
                s.Step();
                if (Debug && k < 60 && k % 3 == 0) Console.WriteLine($"    {k} 불 {ch.BurningCount} · 로봇 {m.Thought(r)} 칸 {m[r].C.Cell} 물 {m[r].C.HoldCharge:0} · 계획 {string.Join(">", m[r].Plan.Select(x => x.Kind + ":" + x.Cell))} [{m[r].C.StepIndex}] · 불 믿음 {string.Join(",", m[r].Beliefs.Where(b => b.Kind == BeliefKind.Fire).Select(b => $"{b.Cell}:{b.Conf:0.0}"))}");
            }
            var ext = s.Facts.Since(0).Where(f => f.Kind == FactKind.Extinguished).ToList();
            return (ext.Count > 0 && ext[0].Actor == r, s.Facts.CountOf(FactKind.ComputerAlarm), s.Facts.CountOf(FactKind.ComputerAssign), ch.BurningCount);
        }
        var ok = FireRun(true);
        var off = FireRun(false);
        Check("빈 방의 불: 주컴퓨터가 연기 · 열로 알아채 경보를 울리고 다른 방 로봇을 보내 끈다", ok.robotOut && ok.alarms > 0 && ok.assigns > 0,
            $"로봇이 끔 {ok.robotOut} · 경보 {ok.alarms} · 맡김 {ok.assigns}");
        Check("주컴퓨터가 멈춰 있으면 빈 방의 불을 아무도 모른다 (로봇이 오지 않는다)", !off.robotOut && off.alarms == 0 && off.assigns == 0,
            $"로봇이 끔 {off.robotOut} · 경보 {off.alarms} · 아직 타는 칸 {off.burning}");

        // ── 일산화탄소: 냄새는 없어도 주컴퓨터 센서가 잡는다 → 경보가 잠든 사람을 깨워 내보낸다 ──
        {
            var s = World();
            var m = s.Get<Mind>(); var f = s.Get<Field>(); var life = s.Get<Life>();
            int sleeper = m.AddPerson(s, At(3, 5), -1, At(3, 5));
            ref var sb = ref life[m[sleeper].C.Body];
            sb.Asleep = 1; m[sleeper].C.Sleepy = 1;
            m[sleeper].Plan.Add(new PlanStep { Kind = StepKind.Use, Arg = (byte)UseKind.Sleep, Value = Time.Hours(8) });
            m[sleeper].C.Goal = GoalKind.Sleep;
            foreach (int c in lay.Rooms[lay.Room[At(6, 2)]].Cells) f.AddGas(c, f.T.gCO, f.Moles(c) * 0.004, 293);
            float cohb = 0;
            for (int k = 0; k < Time.Minutes(20); k++)
            {
                s.Step(); cohb = Math.Max(cohb, life[m[sleeper].C.Body].COHb);
                if (Debug && k % 20 == 0) Console.WriteLine($"    {k} 잠든 이 칸 {m[sleeper].C.Cell} {m.Thought(sleeper)} 자는 중 {life[m[sleeper].C.Body].Asleep} · 믿음 {string.Join(",", m[sleeper].Beliefs.Select(b => $"{b.Kind}@{b.Cell}:{b.Conf:0.0}/{b.Src}"))} · 회피 {string.Join(",", m[sleeper].Avoid.Select(v => v.Goal + ":" + v.Target))}");
            }
            var cm = s.Get<ShipMind>();
            if (Debug) for (int rr = 0; rr < lay.Rooms.Count; rr++) Console.WriteLine($"    방 {lay.Rooms[rr].Name}: {cm.RoomKPa[rr]:0.0}kPa 산소 {cm.RoomO2[rr]:0.0} 이산화탄소 {cm.RoomCO2[rr]:0} 일산화탄소 {cm.RoomCO[rr]:0} 그을음 {cm.RoomSoot[rr]:0} 온도 {cm.RoomTemp[rr]:0} 읽힘 {cm.RoomSeen[rr]}");
            if (Debug) foreach (var x in cm.Incidents) Console.WriteLine($"    사건 {x.Kind} 방 {x.Room} 활성 {x.Active} 수준 {x.Level:0.0}");
            int room = m.RoomOf(m[sleeper].C.Cell);
            Check("일산화탄소: 주컴퓨터 경보가 잠든 사람을 깨운다 → 방을 나간다", s.Facts.CountOf(FactKind.Woke) > 0 && room != lay.Room[At(6, 2)] && life[m[sleeper].C.Body].Alive != 0,
                $"깸 {s.Facts.CountOf(FactKind.Woke)} · 있는 곳 {(room >= 0 ? lay.Rooms[room].Name : "-")} · 가장 높던 일산화탄소 헤모글로빈 {cohb:0.00} · 경보 {s.Facts.CountOf(FactKind.ComputerAlarm)}");
        }

        // ── 구멍 난 빈 방: 주컴퓨터가 기압이 떨어지는 것을 보고 봉쇄한다 → 옆 방은 버틴다 (주컴퓨터가 멈춰 있으면 같이 빠진다) ──
        (double right, long seals) BreachRun(bool computerOk)
        {
            var s = World();
            var m = s.Get<Mind>(); var f = s.Get<Field>(); var st = s.Get<Structure>(); var net = s.Get<Network>();
            m.AddRobot(s, At(3, 7), AgentKind.Robot, At(3, 7));
            if (!computerOk) { int dev = net.Devices.First(d => d.Type == "MainComputer").Id; net.Age(dev, Part.Electric, 1.0); }
            int door = lay.DoorAt[At(10, 3)];
            st.RequestDoor(door, 1); s.Run(3); // 옆 방으로 가는 문이 열려 있다
            f.SetHole(At(0, 2), 0.02f);
            for (int k = 0; k < Time.Minutes(8); k++)
            {
                s.Step();
                if (Debug && k < 40 && k % 4 == 0) Console.WriteLine($"    {k} 왼 {f.Pressure(At(6, 2)):0.0} 오른 {f.Pressure(At(15, 3)):0.0} 통로 {f.Pressure(At(10, 8)):0.0} · 문 {string.Join(",", f.DoorOpen.Select(x => x.ToString("0.0")))}");
            }
            return (f.Pressure(At(15, 3)), s.Facts.CountOf(FactKind.ComputerSeal));
        }
        var on = BreachRun(true); var down = BreachRun(false);
        Check("구멍 난 빈 방: 주컴퓨터가 기압이 떨어지는 것을 보고 봉쇄한다 → 열려 있던 옆 방이 버틴다 (주컴퓨터가 멈추면 같이 빠진다)",
            on.seals > 0 && on.right > 60 && down.right < 20 && down.seals == 0,
            $"옆 방: 주컴퓨터 있음 {on.right:0}kPa (봉쇄 {on.seals}) · 멈춤 {down.right:0}kPa");

        // ── 상상: 사람이 안에 있는 방을 봉쇄할지 — 세계를 복제해 두 안을 2분씩 돌려 보고 고른다 ──
        {
            // (가) 뚫린 방에 쓰러진 사람 · 열린 문 너머 옆 방에도 쓰러진 사람 → 지금 닫는 쪽이 옆 방 사람을 살린다
            var s = World();
            var m = s.Get<Mind>(); var f = s.Get<Field>(); var st = s.Get<Structure>(); var life = s.Get<Life>(); var cm = s.Get<ShipMind>();
            int a = m.AddPerson(s, At(6, 2)), b = m.AddPerson(s, At(13, 2));
            foreach (int p in new[] { a, b }) { ref var body = ref life[m[p].C.Body]; body.Conscious = 0; body.Hypoxia = 0.6f; }
            int door = lay.DoorAt[At(10, 3)];
            st.RequestDoor(door, 1); s.Run(3);
            f.SetHole(At(0, 2), 0.05f);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            s.Run(Time.Minutes(3));
            double ms = sw.Elapsed.TotalMilliseconds;
            var im = cm.Imaginations.Where(v => v.Room == lay.Room[At(6, 2)]).ToList();
            bool chosenNow = im.Any(v => v.Option == 1 && v.Chosen == 1);
            Check("상상 (가): 뚫린 방에 쓰러진 사람 · 열린 문 너머에도 쓰러진 사람 — 두 미래를 돌려 보고 '지금 닫는다'를 고른다 → 옆 방 사람이 산다",
                chosenNow && life[m[b].C.Body].Alive != 0 && f.Pressure(life[m[b].C.Body].Cell) > 50,
                string.Join(" / ", im.Select(v => $"{(v.Option == 1 ? "지금 닫음" : "기다림")}: 죽음 {v.Dead} 쓰러짐 {v.Down} 잃은 기압 {v.LostKPa:0}{(v.Chosen == 1 ? " ← 고름" : "")}"))
                + $" · 옆 방 사람 곁 {f.Pressure(life[m[b].C.Body].Cell):0}kPa · 3분 계산 {ms:0}ms");

            // (나) 천천히 새는 방 · 깨어 있는 사람 → 먼저 내보내고 봉쇄한다 (사람이 갇히지 않는다)
            var s2 = World();
            var m2 = s2.Get<Mind>(); var f2 = s2.Get<Field>(); var life2 = s2.Get<Life>(); var cm2 = s2.Get<ShipMind>();
            int c2 = m2.AddPerson(s2, At(2, 1));
            s2.Run(3);
            f2.SetHole(At(0, 4), 0.004f);
            for (int k = 0; k < Time.Minutes(6); k++)
            {
                s2.Step();
                if (Debug && k < 150 && k % 5 == 0) Console.WriteLine($"    {k} 사람 칸 ({lay.X(m2[c2].C.Cell) - 1},{lay.Y(m2[c2].C.Cell) - 1}) {m2.Thought(c2)} 모드 {m2[c2].C.Mode} · 여기 {f2.Pressure(m2[c2].C.Cell):0} · 문 {string.Join(",", f2.DoorOpen.Select(x => x.ToString("0.0")))} · 계획 {string.Join(">", m2[c2].Plan.Select(x => x.Kind + ":" + x.Cell))}[{m2[c2].C.StepIndex}] 길 {m2[c2].Path.Count}/{m2[c2].PathIdx} 기다림 {m2[c2].C.WaitTicks}");
            }
            var seal = s2.Facts.Since(0).Where(x => x.Kind == FactKind.ComputerSeal && x.Other == lay.Room[At(6, 2)]).ToList();
            int r2 = m2.RoomOf(m2[c2].C.Cell);
            bool patched2 = f2.Hole[At(0, 4)] <= 0;
            Check("상상 (나): 천천히 새는 방에 깨어 있는 사람 — 나가거나 직접 막는다 · 봉쇄는 사람이 나간 뒤거나 막는 사람이 남아 있을 때만 · 산다",
                life2[m2[c2].C.Body].Alive != 0 && seal.Count > 0 && (seal[0].Size == 0 || patched2),
                $"사람 {(r2 >= 0 ? lay.Rooms[r2].Name : "-")} {f2.Pressure(m2[c2].C.Cell):0}kPa {m2.Thought(c2)} · 직접 막음 {patched2} · 봉쇄 {seal.Count} (안에 사람 {(seal.Count > 0 ? seal[0].Size : -1)}) · 상상 {cm2.Imaginations.Count}");
        }

        // ── 차단기: 합선 없이 떨어진 차단기는 주컴퓨터가 다시 올린다 ──
        {
            var s = World();
            var net = s.Get<Network>();
            s.Run(5);
            int fd = net.RoomFeeder[lay.Room[At(15, 3)]];
            net.Breaker[fd] = 0;
            s.Run(Time.Minutes(1));
            Check("합선 없이 떨어진 차단기는 주컴퓨터가 다시 올린다", net.Breaker[fd] == 1 && s.Facts.CountOf(FactKind.BreakerReset) > 0, $"차단기 {net.Breaker[fd]}");
        }

        // ── 쓰러진 사람: 사람 표로 알고 다른 방 로봇을 보낸다 → 업어 낸다 ──
        {
            var s = World();
            var m = s.Get<Mind>(); var f = s.Get<Field>(); var life = s.Get<Life>();
            int v = m.AddPerson(s, At(15, 2));
            int r = m.AddRobot(s, At(3, 7), AgentKind.Robot, At(3, 7));
            foreach (int c in lay.Rooms[lay.Room[At(15, 2)]].Cells) { double o2 = f.Gas(f.T.gO2, c); f.RemoveGas(c, f.T.gO2, o2); f.AddGas(c, f.T.gN2, o2, 293); }
            ref var vb = ref life[m[v].C.Body];
            vb.Conscious = 0; vb.Hypoxia = 0.8f;
            for (int k = 0; k < Time.Minutes(8) && s.Facts.CountOf(FactKind.Rescue) == 0; k++)
            {
                s.Step();
                if (Debug && k % 10 == 0) Console.WriteLine($"    {k} 로봇 {m.Thought(r)} 칸 {m[r].C.Cell}");
            }
            var body = life[m[v].C.Body];
            Check("쓰러진 사람: 주컴퓨터가 사람 표로 알고 다른 방 로봇을 보낸다 → 업어 낸다", s.Facts.CountOf(FactKind.Rescue) > 0 && s.Facts.CountOf(FactKind.ComputerAssign) > 0 && body.Alive != 0,
                $"구함 {s.Facts.CountOf(FactKind.Rescue)} · 맡김 {s.Facts.CountOf(FactKind.ComputerAssign)} · 내려놓은 곳 산소 {f.Partial(f.T.gO2, body.Cell):0.0}kPa");
        }

        // ── 옆 방이 진공: 사이 문틈으로 새고 · 문이 끼익거린다 → 로봇이 문틈을 막고 덧대 받친다 · 사람은 진공 쪽으로 나가지 않는다 ──
        {
            var s = World(3);
            var m = s.Get<Mind>(); var f = s.Get<Field>(); var st = s.Get<Structure>(); var life = s.Get<Life>();
            int r1 = m.AddRobot(s, At(3, 7), AgentKind.Robot, At(3, 7));
            int r2 = m.AddRobot(s, At(8, 7), AgentKind.Robot, At(8, 7));
            int p = m.AddPerson(s, At(5, 2), -1, -1);
            s.Run(5);
            int hull = At(17, 0);
            f.SetHole(hull, 2.5f); // 오른쪽 방 천장 외판이 통째로 떨어져 나갔다
            st.DamageCause[hull] = s.Causes.Add(CauseKind.Story, "오른쪽 방 외판이 떨어져 나갔다", s.Tick, hull);
            var left = s.Layout.Rooms[s.Layout.Room[At(5, 2)]].Cells;
            int door = At(10, 3), doorC = At(14, 6);
            double minLeft = 999, minSpO2 = 1;
            for (int k = 0; k < Time.Minutes(30); k++)
            {
                s.Step();
                minLeft = Math.Min(minLeft, left.Average(c => f.Pressure(c)));
                minSpO2 = Math.Min(minSpO2, life[m[p].C.Body].SpO2);
            }
            var taped = s.Facts.Since(0).Where(x => x.Kind == FactKind.Patched && (x.Cell == door || x.Cell == doorC)).ToList();
            Check("옆 방이 진공: 로봇이 쉬익대는 문틈을 막아 이쪽 방 기압이 버틴다", taped.Count > 0 && minLeft > 60,
                $"문틈 막음 {taped.Count} ({string.Join(", ", taped.Select(x => s.Causes.Text(x.Cause)))}) · 왼쪽 방 최저 {minLeft:0}kPa");
            Check("옆 방이 진공: 사람은 새는 문틈 쪽 진공으로 뛰쳐나가지 않는다", minSpO2 > 0.85 && life[m[p].C.Body].Alive != 0,
                $"가장 낮은 산소 포화 {minSpO2:0.00} · 지금 {m.RoomOf(m[p].C.Cell)}방 · {m.Thought(p)}");
            for (int k = 0; k < Time.Hours(6); k++) s.Step();
            Check("옆 방이 진공: 끼익거리는 문을 덧대 받쳐 6시간을 버틴다", s.Facts.CountOf(FactKind.DoorBlown) == 0 && st.Braced[door] != 0,
                $"문 날아감 {s.Facts.CountOf(FactKind.DoorBlown)} · 받침 {st.Braced[door]}/{st.Braced[doorC]} · 피로 {st.Fatigue[door]:0.00} · 부하 {st.Stress[door]:0.00}");
        }

        // ── 정전: 주컴퓨터 방의 급전선이 합선으로 떨어져도 자체 축전지로 버틴다 (3kWh · 약 70분) ──
        {
            var s = World(4);
            var net = s.Get<Network>(); var sm = s.Get<ShipMind>();
            s.Run(10);
            var comp = net.Devices.First(d => d.Type == "MainComputer");
            int fd = comp.Feeder;
            net.Breaker[fd] = 0; net.FaultCell[fd] = comp.Cell; // 합선이 남아 있어 다시 올리지 못한다
            bool on30 = true;
            for (int k = 0; k < Time.Minutes(30); k++) { s.Step(); if (!sm.Online) on30 = false; }
            for (int k = 0; k < Time.Minutes(60); k++) s.Step();
            Check("정전: 주컴퓨터는 급전선이 끊겨도 자체 축전지로 버틴다 (30분 내내 켜짐 · 전지가 다하면 꺼진다)", on30 && !sm.Online,
                $"30분 켜짐 {on30} · 90분 뒤 {(sm.Online ? "켜짐" : "꺼짐")} · 남은 전지 {net.Charge[comp.Id]:0.00}kWh");
        }

        // ── 눌어붙은 차단기의 합선: 주컴퓨터가 로봇을 배전반으로 보내 손으로 끊는다 (하던 일도 놓고) ──
        {
            var s = World(6);
            var m = s.Get<Mind>(); var net = s.Get<Network>();
            int r = m.AddRobot(s, At(3, 7), AgentKind.Robot, At(3, 7));
            s.Run(10);
            m.Assign(s, r, GoalKind.Investigate, At(20, 7), new Belief { Kind = BeliefKind.Bang, Cell = At(20, 7), Subject = -1, Conf = 0.5f }, s.Tick + Time.Minutes(30)); // 다른 일을 맡고 있었다
            var dev = net.Devices.First(d => d.Feeder >= 0 && d.Room == s.Layout.Room[At(17, 2)]);
            int fd = dev.Feeder;
            net.BreakerStuck[fd] = 1; net.FaultCell[fd] = dev.Cell;
            long pulledAt = -1;
            for (int k = 0; k < Time.Minutes(15) && pulledAt < 0; k++) { s.Step(); if (net.Breaker[fd] == 0) pulledAt = s.Tick; }
            var fact = s.Facts.Since(0).Where(f => f.Kind == FactKind.Repaired).Select(f => s.Causes.Text(f.Cause)).FirstOrDefault() ?? "";
            Check("눌어붙은 차단기의 합선: 로봇이 하던 일을 놓고 배전반에서 그 급전선을 손으로 끊는다", pulledAt > 0 && fact.Contains("손으로 끊었다"),
                pulledAt > 0 ? $"{(pulledAt - 10) / 25.0:0.0}분 만에 · {fact}" : "끊지 못했다");
        }

        // ── 빈 방 외판의 작은 금(5cm²): 쉬익 소리를 들을 사람이 없어도 주컴퓨터가 배 전체의 느린 샘을 보고 로봇을 보내 막는다 ──
        {
            var s = ShipWorld.Create("Mirinae", 3);
            var m = s.Get<Mind>(); var f = s.Get<Field>();
            int dock = s.Layout.Furniture.First(fu => fu.Type == "RobotDock").Cells[0];
            m.AddRobot(s, dock, AgentKind.Robot, dock);
            s.Run(Time.Minutes(15));
            var lay2 = s.Layout;
            int engineRoom = lay2.Rooms.First(r => r.Type == "Engine").Id;
            int hull = Enumerable.Range(0, lay2.N).First(c => lay2.Hull[c] && lay2.Kind[c] == CellKind.Wall && Enumerable.Range(0, 4).Any(k =>
            {
                int x = lay2.X(c) + (k == 0 ? 1 : k == 1 ? -1 : 0), y = lay2.Y(c) + (k == 2 ? 1 : k == 3 ? -1 : 0);
                return lay2.In(x, y) && lay2.Room[lay2.Index(x, y)] == engineRoom;
            }));
            f.SetHole(hull, 0.0005f);
            long patchedAt = -1;
            for (int k = 0; k < Time.Hours(1) && patchedAt < 0; k++) { s.Step(); if (f.Hole[hull] <= 0) patchedAt = s.Tick; }
            Check("빈 방 외판의 작은 금: 들을 사람이 없어도 주컴퓨터가 배 전체의 느린 샘을 보고 로봇을 보내 막는다 (한 시간 안)", patchedAt > 0,
                patchedAt > 0 ? $"{(patchedAt - Time.Minutes(15)) / 25.0:0}분 만에 막음" : $"못 막음 · 평균 기압 {s.Get<ShipMind>().RoomKPa.Where(x => x > 0).Average():0}kPa");
        }
    }
}
