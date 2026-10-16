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
    }
}
