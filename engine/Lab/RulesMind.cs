using ShipEngine;

namespace Lab;

/// <summary>⑧ 마음 규칙 시험 — 작은 시험 배(Worlds.MindShip)에서 하나씩.</summary>
public static partial class Program
{
    private static void MindRules()
    {
        if (!Do("마음")) return;
        var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        Sim World(ulong seed = 1) => Worlds.Full(lay, seed);
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";

        // ── 불: 사람이 보고 외친다 · 같은 방 로봇이 소화기를 들고 먼저 끈다 · 사람은 방을 빠져나간다 ──
        {
            var s = World();
            var m = s.Get<Mind>(); var ch = s.Get<Chemistry>(); var life = s.Get<Life>();
            int p = m.AddPerson(s, At(12, 2));
            int r = m.AddRobot(s, At(22, 4), AgentKind.Robot, At(22, 4));
            s.Run(3);
            ch.Ignite(At(15, 1), 3e6, s.Causes.Add(CauseKind.Story, "의자 밑에서 불씨가 튀었다", s.Tick));
            long outAt = -1;
            for (int k = 0; k < Time.Minutes(15) && s.Facts.CountOf(FactKind.Extinguished) == 0; k++)
            {
                s.Step();
                if (outAt < 0 && k > 1 && ch.BurningCount == 0) outAt = s.Tick;
                if (Debug && (k < 12 || k % 10 == 0)) Console.WriteLine($"    {k} 불 {ch.BurningCount} 겉면 {s.Get<Field>().Tf[At(15, 1)]:0}K 고분자 {s.Get<Field>().Stored(s.Get<Field>().T.sPoly, At(15, 1)):0} · 사람 {m.Thought(p)} 칸 {m[p].C.Cell} · 로봇 {m.Thought(r)} 칸 {m[r].C.Cell} 물 {m[r].C.HoldCharge:0}");
            }
            var ext = s.Facts.Since(0).Where(f => f.Kind == FactKind.Extinguished).ToList();
            bool robotFirst = ext.Count > 0 && ext[0].Actor == r;
            bool warned = s.Facts.CountOf(FactKind.Warned) > 0;
            bool personOut = m.RoomOf(m[p].C.Cell) != m.RoomOf(At(15, 1)) || m[p].C.Goal == GoalKind.Evacuate;
            Check("불: 사람이 보고 외친다 · 로봇이 소화기를 들고 먼저 끈다 · 사람은 불 끄기를 로봇에 맡긴다",
                outAt > 0 && robotFirst && warned && life[m[p].C.Body].Alive != 0 && m[p].C.Goal != GoalKind.FightFire,
                $"꺼진 때 {(outAt > 0 ? (outAt - 3) / 25.0 : -1):0.0}분 · 끈 이 {(ext.Count > 0 ? m.NameOf(ext[0].Actor) : "-")} · 외침 {s.Facts.CountOf(FactKind.Warned)} · 사람 {m.Thought(p)} · 나감 {personOut}");
        }

        // ── 구멍: 쉬익 소리를 듣고 방을 빠져나와 문을 닫는다 → 산다 ──
        {
            var s = World();
            var m = s.Get<Mind>(); var f = s.Get<Field>(); var life = s.Get<Life>();
            int p = m.AddPerson(s, At(6, 2));
            s.Run(3);
            int seed = s.Causes.Add(CauseKind.Story, "외판 이음매가 벌어졌다", s.Tick);
            f.SetHole(At(0, 2), 0.003f);
            s.Get<Structure>().DamageCause[At(0, 2)] = seed;
            for (int k = 0; k < Time.Minutes(10); k++)
            {
                s.Step();
                if (Debug && k < 120 && k % 3 == 0) Console.WriteLine($"    {k} 칸 {m[p].C.Cell} ({lay.X(m[p].C.Cell) - 1},{lay.Y(m[p].C.Cell) - 1}) {m.Thought(p)} 모드 {m[p].C.Mode} · 여기 {f.Pressure(m[p].C.Cell):0} · 뚫린 방 {f.Pressure(At(6, 2)):0} · 문 {string.Join(",", f.DoorOpen.Select(x => x.ToString("0.0")))} · 믿음 {string.Join(",", m[p].Beliefs.Select(b => $"{b.Kind}@{b.Cell}:{b.Conf:0.0}"))}");
            }
            var body = life[m[p].C.Body];
            int room = m.RoomOf(m[p].C.Cell);
            bool sealedOut = room != m.RoomOf(At(6, 2)) && f.Pressure(m[p].C.Cell) > 85 && s.Facts.CountOf(FactKind.DoorSealed) is > 0 and <= 4;
            bool patched = f.Hole[At(0, 2)] <= 0 && s.Facts.CountOf(FactKind.Patched) > 0;
            Check("구멍: 방을 빠져나와 문을 닫거나(문 닫기 몇 번 안에) · 봉합 꾸러미로 직접 막는다 → 산다",
                body.Alive != 0 && body.Conscious != 0 && (sealedOut || patched),
                $"있는 곳 {(room >= 0 ? lay.Rooms[room].Name : "-")} {f.Pressure(m[p].C.Cell):0}kPa · 뚫린 방 {f.Pressure(At(6, 2)):0}kPa · 문 닫음 {s.Facts.CountOf(FactKind.DoorSealed)} · 막음 {patched} · 산소 포화 {body.SpO2:0.00} · {m.Thought(p)}");
        }

        // ── 일산화탄소: 냄새가 없다 — 깨어 있는 사람은 두통으로 '공기가 이상하다'만 알고 방을 나간다 · 잠든 사람은 깨지 않는다 ──
        {
            var s = World();
            var m = s.Get<Mind>(); var f = s.Get<Field>(); var life = s.Get<Life>();
            int awake = m.AddPerson(s, At(6, 2));
            int sleeper = m.AddPerson(s, At(3, 5), -1, At(3, 5));
            ref var sb = ref life[m[sleeper].C.Body];
            sb.Asleep = 1;
            m[sleeper].C.Sleepy = 1;
            m[sleeper].Plan.Add(new PlanStep { Kind = StepKind.Use, Arg = (byte)UseKind.Sleep, Value = Time.Hours(8) });
            m[sleeper].C.Goal = GoalKind.Sleep;
            foreach (int c in lay.Rooms[lay.Room[At(6, 2)]].Cells) f.AddGas(c, f.T.gCO, f.Moles(c) * 0.004, 293); // 4000ppm
            bool smelled = false, badAir = false, sleptOn = true; float cohb = 0;
            for (int k = 0; k < Time.Minutes(40); k++)
            {
                s.Step();
                smelled |= m[awake].Beliefs.Any(b => b.Kind == BeliefKind.SmokeSmell);
                badAir |= m[awake].Beliefs.Any(b => b.Kind == BeliefKind.BadAir);
                if (life[m[sleeper].C.Body].Conscious != 0 && life[m[sleeper].C.Body].Asleep == 0 && life[m[sleeper].C.Body].Alive != 0) sleptOn = false;
                cohb = Math.Max(cohb, life[m[sleeper].C.Body].COHb);
            }
            int room = m.RoomOf(m[awake].C.Cell);
            Check("일산화탄소: 냄새로는 모른다 · 깨어 있는 사람은 두통으로 '공기가 이상하다'고 느껴 방을 나간다 · 잠든 사람은 깨지 않는다",
                !smelled && badAir && room != lay.Room[At(6, 2)] && sleptOn && cohb > 0.1f,
                $"냄새 {smelled} · 이상한 공기 {badAir} · 깬 사람 {(room >= 0 ? lay.Rooms[room].Name : "-")} · 잠든 사람 계속 잠 {sleptOn} (일산화탄소 헤모글로빈 {cohb:0.00})");
        }

        // ── 목마름: 자다가 목이 몹시 마르면 깨어 물을 마시고 · 다시 눕는다 (⑦ 오줌 · 숨으로 밤새 물을 잃는다) ──
        {
            var s = World();
            var m = s.Get<Mind>(); var life = s.Get<Life>();
            int p = m.AddPerson(s, At(3, 5), -1, At(3, 5));
            ref var pb = ref life[m[p].C.Body];
            pb.Asleep = 1; pb.Water = Mind.BodyWater0 * (1 - 0.02 * 0.9);   // 몸의 물 1.8% 모자람 (0.76L)
            m[p].C.Sleepy = 1; m[p].C.Hunger = 0;
            m[p].Plan.Add(new PlanStep { Kind = StepKind.Use, Arg = (byte)UseKind.Sleep, Value = Time.Hours(8) });
            m[p].C.Goal = GoalKind.Sleep;
            long drank = -1, back = -1, drank0 = m[p].C.DrankAt;
            for (int k = 0; k < Time.Hours(2) && back < 0; k++)
            {
                s.Step();
                if (drank < 0 && m[p].C.DrankAt != drank0) drank = k;
                if (drank >= 0 && life[m[p].C.Body].Asleep != 0) back = k;
            }
            Check("목마름: 자다가 목이 몹시 마르면 깨어 물을 마시고 다시 잔다",
                drank > 0 && drank < Time.Hours(1) && back > drank,
                $"마신 때 {(drank > 0 ? drank / 25.0 : -1):0}분 · 다시 잔 때 {(back > 0 ? back / 25.0 : -1):0}분 · 목마름 {m[p].C.Thirst:0.00} · {m.Thought(p)}");
        }

        // ── 구조: 산소 없는 방에 쓰러진 사람 — 로봇이 보고 업어 공기 좋은 곳에 내려놓는다 ──
        {
            var s = World();
            var m = s.Get<Mind>(); var f = s.Get<Field>(); var life = s.Get<Life>();
            int v = m.AddPerson(s, At(6, 4));
            int r = m.AddRobot(s, At(8, 5), AgentKind.Robot, At(8, 5));
            foreach (int c in lay.Rooms[lay.Room[At(6, 2)]].Cells) { double o2 = f.Gas(f.T.gO2, c); f.RemoveGas(c, f.T.gO2, o2); f.AddGas(c, f.T.gN2, o2, 293); }
            ref var vb = ref life[m[v].C.Body];
            vb.Conscious = 0; vb.Hypoxia = 0.8f;
            for (int k = 0; k < Time.Minutes(6); k++)
            {
                s.Step();
                if (Debug && k < 80 && k % 2 == 0) Console.WriteLine($"    {k} 로봇 칸 {m[r].C.Cell} {m.Thought(r)} 업음 {m[r].C.Carrying} · 쓰러진 이 칸 {life[m[v].C.Body].Cell} 산소 {f.Partial(f.T.gO2, life[m[v].C.Body].Cell):0.0} · 계획 {string.Join(">", m[r].Plan.Select(x => x.Kind + ":" + x.Cell))} [{m[r].C.StepIndex}]");
            }
            var vbody = life[m[v].C.Body];
            Check("구조: 산소 없는 방에 쓰러진 사람을 로봇이 업어 공기 좋은 곳에 내려놓는다",
                s.Facts.CountOf(FactKind.Rescue) > 0 && f.Partial(f.T.gO2, vbody.Cell) > 17 && vbody.Alive != 0,
                $"구함 {s.Facts.CountOf(FactKind.Rescue)} · 내려놓은 곳 산소 {f.Partial(f.T.gO2, vbody.Cell):0.0}kPa · 살아 있음 {vbody.Alive} · 로봇 {m.Thought(r)}");
        }

        // ── 틀린 믿음: 말로 들은 불(실제로는 없음)을 끄러 갔다가, 와서 보고 지운다 ──
        {
            var s = World();
            var m = s.Get<Mind>();
            int p = m.AddPerson(s, At(15, 3));
            m[p].C.Brave = 1; m[p].C.SkFire = 1;
            int fake = At(4, 1);
            m.Learn(m[p], s.Tick, BeliefKind.Fire, fake, -1, 0.9f, BeliefSource.Told);
            int nearest = 99; bool wentFor = false;
            for (int k = 0; k < Time.Minutes(10); k++)
            {
                s.Step();
                nearest = Math.Min(nearest, m.Dist(m[p].C.Cell, fake));
                wentFor |= m[p].C.Goal == GoalKind.FightFire;
            }
            bool gone = !m[p].Beliefs.Any(b => b.Kind == BeliefKind.Fire && b.Conf > 0.1f);
            Check("틀린 믿음: 말로 들은 불을 끄러 갔다가 · 와서 보고 없으면 지운다", wentFor && nearest <= 3 && gone,
                $"불 끄러 감 {wentFor} · 가장 가까이 {nearest}칸 · 믿음 지움 {gone}");
        }

        // ── 안 열리는 문: 끼인 문을 만나면 '안 열린다'고 알고 다른 길로 간다 ──
        {
            var s = World();
            var m = s.Get<Mind>(); var st = s.Get<Structure>();
            int p = m.AddPerson(s, At(2, 5));
            int door = lay.DoorAt[At(5, 6)];
            st.DoorJam[door] = 1;
            m[p].C.Hunger = 1;
            long ate = 0;
            for (int k = 0; k < Time.Minutes(30) && ate == 0; k++) { s.Step(); ate = m[p].C.AteAt; }
            bool stuck = m[p].Beliefs.Any(b => b.Kind == BeliefKind.StuckDoor && b.Subject == door);
            Check("안 열리는 문: 끼인 문을 '안 열린다'고 알고 다른 길로 돌아가 밥을 먹는다", ate > 0 && stuck,
                $"먹은 때 {(ate > 0 ? ate / 25.0 : -1):0.0}분 · 문 믿음 {stuck} · {m.Thought(p)}");
        }
    }
}
