using ShipEngine;

namespace Lab;

/// <summary>판단 이관 (사) 기계 몸 — 로봇 · 주컴퓨터의 몸은 세상(⑥ 망)이 계산하고 판단 엔진은 느끼고 대응만 한다 (docs/ENGINE.md 판단 이관 (사)).</summary>
public static partial class Program
{
    private static void MachineRules()
    {
        if (!Do("기계 몸")) return;
        Console.WriteLine("\n[기계 몸]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);

        // ── 맨몸 (마음 없이 망에 놓은 로봇 몸 — 세상만): 열 · 충격 · 진공 · 전지 ──
        {
            var s = Worlds.Full(lay, 5);
            var net = s.Get<Network>(); var f = s.Get<Field>(); var st = s.Get<Structure>(); var ch = s.Get<Chemistry>();
            s.Run(3);
            int hot = At(17, 2), bump = At(5, 8), vac = At(20, 8), calm = At(12, 2), idle = At(12, 4);
            int heat = s.Causes.Add(CauseKind.Fire, "옆 칸 불이 그 칸을 달군다", s.Tick, hot);
            int hit = s.Causes.Add(CauseKind.Impact, "파편 한 줌이 그 자리를 쳤다", s.Tick, bump);
            int hb = net.AddRobot(hot), ib = net.AddRobot(bump), vb = net.AddRobot(vac), wb = net.AddRobot(calm), qb = net.AddRobot(idle);
            long eyesAt = -1, coreAt = -1;
            for (int k = 0; k < Time.Minutes(12); k++)
            {
                f.AddHeat(hot, f.GasC(hot) * (470 - f.Temp(hot))); ch.HeatCause[hot] = heat;   // 공기 470K
                net.Robot(vb).Load = 1; net.Robot(wb).Load = 1;                                  // 둘은 힘을 쓴다 (하나는 진공에서)
                for (int g = 0; g < f.G; g++) { double n = f.Gas(g, vac); if (n > 0) f.RemoveGas(vac, g, n); }
                if (k == 10) { st.ImpactJ[bump] += 8000; st.DamageCause[bump] = hit; }
                s.Step();
                if (eyesAt < 0 && net.Robot(hb).Eyes >= 1) eyesAt = k;
                if (coreAt < 0 && net.Robot(hb).Core >= 1) coreAt = k;
            }
            var H = net.Robot(hb); var I = net.Robot(ib); var V = net.Robot(vb); var W = net.Robot(wb); var Q = net.Robot(qb);
            string heatWhy = s.Causes.Story(H.CoreCause), hitWhy = s.Causes.Story(I.LegsCause);
            if (Debug) Console.WriteLine($"    열: 눈 {Time.ToSeconds(eyesAt):0}초 · 속 {Time.ToSeconds(coreAt):0}초 · {heatWhy} / 충격: 다리 {I.Legs:0.00} 팔 {I.Arms:0.00} 속 {I.Core:0.00} · {hitWhy} / 진공 속 {V.Core:0.000} · 숨쉬는 칸 {W.Core:0.000} / 전지 힘씀 {W.Battery:0.000} · 쉼 {Q.Battery:0.000}");
            Check("맨몸 (세상이 계산): 470K 공기에 센서가 먼저 흐려지고(5분) 속이 다 달아 멈춘다(10분 — 원인 사슬이 그 열기까지) · 8kJ 충격에 다리 0.5 · 팔 0.33 · 속 0.17 · 진공에서 힘을 쓰면 속이 단다 (숨쉬는 칸은 아니다) · 힘쓰는 몸은 전지가 빨리 준다",
                eyesAt > 0 && coreAt > eyesAt && heatWhy.Contains("옆 칸 불") && Math.Abs(I.Legs - 0.5f) < 0.02f && Math.Abs(I.Arms - 1 / 3f) < 0.02f && Math.Abs(I.Core - 1 / 6f) < 0.02f
                && hitWhy.Contains("파편 한 줌") && V.Core > 0.03f && W.Core == 0 && W.Battery < Q.Battery,
                $"눈 {Time.ToSeconds(eyesAt):0}초 · 속 {Time.ToSeconds(coreAt):0}초 · 충격 다리 {I.Legs:0.00} 팔 {I.Arms:0.00} 속 {I.Core:0.00} · 진공 속 {V.Core:0.000} / 숨쉬는 칸 {W.Core:0.000} · 전지 {W.Battery:0.000} < {Q.Battery:0.000}");
        }

        // ── 감전: 물이 고인 바닥에 전기가 흐르면 그 위의 로봇이 감전된다 (속 +0.3 · 다리 +0.2) · 마른 바닥의 로봇은 아니다 ──
        {
            var s = Worlds.Full(lay, 5);
            var net = s.Get<Network>(); var f = s.Get<Field>();
            s.Run(3);
            int fd = -1, wet = -1, dry = -1;
            for (int k = 0; k < net.Feeders.Count && wet < 0; k++)
            {
                var floor = net.Feeders[k].Path.Where(c => lay.Kind[c] == CellKind.Floor && lay.FurnitureAt[c] < 0).ToList();
                if (floor.Count >= 2) { fd = k; wet = floor[0]; dry = floor[^1]; }
            }
            net.BreakerStuck[fd] = 1;   // 차단기가 눌어붙어 있다 — 젖어도 전기가 남는다
            int wb = net.AddRobot(wet), db = net.AddRobot(dry);
            f.AddStore(wet, f.T.sWater, 15 * 55.5);   // 1.5cm 물
            for (int k = 0; k < 3; k++) { s.Step(); if (Debug) Console.WriteLine($"    틱 {k} 차단기 {net.Breaker[fd]} 눌음 {net.BreakerStuck[fd]} 합선 칸 {net.FaultCell[fd]} 끊김 {net.Cut(fd)} 전기 {net.LiveAt(wet)} 물 {f.Stored(f.T.sWater, wet):0}몰 · 몸 칸 {net.Robot(wb).Cell}={wet} 전지 {net.Robot(wb).Battery:0.00}"); }
            var Wb = net.Robot(wb); var Db = net.Robot(db);
            string why = s.Causes.Story(Wb.CoreCause);
            if (Debug) Console.WriteLine($"    급전선 {fd} · 젖은 칸 속 {Wb.Core:0.00} 다리 {Wb.Legs:0.00} · 마른 칸 속 {Db.Core:0.00} · 감전 {net.RobotShocks} · {why}");
            Check("감전: 물이 고인 바닥에 전기가 흐르면(눌어붙은 차단기) 그 위의 로봇이 감전된다 — 속 +0.3 · 다리 +0.2 (원인 '감전') · 같은 선 위 마른 바닥의 로봇은 아니다",
                fd >= 0 && net.RobotShocks >= 1 && Wb.Core >= 0.3f && Wb.Legs >= 0.2f && why.Contains("감전") && Db.Core == 0,
                $"감전 {net.RobotShocks} · 젖은 칸 속 {Wb.Core:0.00} 다리 {Wb.Legs:0.00} · 마른 칸 속 {Db.Core:0.00} · \"{why}\"");
        }

        // ── 다리: 상한 다리는 느리다 (다리 0.5 → 빠르기 0.65배) · 망가진 다리(1)는 걷지 않고 까닭을 남긴다 ──
        {
            (long arrive, SnagKind snag) Walk(float legs)
            {
                var s = Worlds.Full(lay, 5);
                var m = s.Get<Mind>(); var net = s.Get<Network>();
                int r = m.AddRobot(s, At(3, 8), AgentKind.Robot, At(3, 8));
                s.Run(3);
                net.Robot(m.Agents[r].C.Mech).Legs = legs;
                int far = At(21, 8);
                m.Assign(s, r, GoalKind.Investigate, far, new Belief { Kind = BeliefKind.Bang, Cell = far, Subject = -1, Conf = 0.5f }, s.Tick + Time.Minutes(30));
                long t0 = s.Tick, arrive = -1;
                for (int k = 0; k < Time.Minutes(3) && arrive < 0; k++) { s.Step(); if (m.Dist(m.Agents[r].C.Cell, far) <= 2) arrive = s.Tick - t0; }   // 살피기는 2칸 안에서 멈춘다
                return (arrive, m.Agents[r].C.Snag);
            }
            var okT = Task.Run(() => Walk(0)); var lameT = Task.Run(() => Walk(0.5f)); var gone = Walk(1); var ok = okT.Result; var lame = lameT.Result;
            Check("다리: 성한 로봇보다 다리가 상한(0.5) 로봇이 늦게 닿는다 (빠르기 0.65배) · 다리가 망가진(1) 로봇은 걷지 않고 '다리가 망가졌다'를 남긴다",
                ok.arrive > 0 && lame.arrive > ok.arrive * 1.3 && gone.arrive < 0 && gone.snag == SnagKind.NoLegs,
                $"성함 {Time.ToSeconds(ok.arrive):0}초 · 상함 {Time.ToSeconds(lame.arrive):0}초 · 망가짐 {(gone.arrive >= 0 ? "닿음" : "못 감")} (막힘 {gone.snag})");
        }

        // ── 팔: 팔을 못 쓰는 로봇(0.6)은 곁의 불을 고르지 않고 · 주컴퓨터도 맡기지 않는다 — 멀리 있던 성한 로봇이 가서 끈다 ──
        {
            var s = Worlds.Full(lay, 5);
            var m = s.Get<Mind>(); var net = s.Get<Network>(); var ch = s.Get<Chemistry>(); var sm = s.Get<ShipMind>();
            int near = m.AddRobot(s, At(13, 4), AgentKind.Robot, At(13, 4)), far = m.AddRobot(s, At(3, 8), AgentKind.Robot, At(3, 8));
            s.Run(3);
            net.Robot(m.Agents[near].C.Mech).Arms = 0.6f;
            ch.Ignite(At(15, 4), 3e6, s.Causes.Add(CauseKind.Setup, "의자 밑에서 불씨가 튀었다", s.Tick));
            bool nearTook = false, farTook = false; long outAt = -1;
            for (int k = 0; k < Time.Minutes(15) && outAt < 0; k++)
            {
                s.Step();
                nearTook |= m.Agents[near].C.Goal == GoalKind.FightFire || m.Agents[near].C.AssignedGoal == GoalKind.FightFire && m.Agents[near].C.AssignedUntil > s.Tick;
                farTook |= m.Agents[far].C.Goal == GoalKind.FightFire;
                if (ch.Ignitions > 0 && ch.BurningCount == 0) outAt = s.Tick;
            }
            if (Debug) Console.WriteLine($"    곁 로봇 불로 감 {nearTook} · 먼 로봇 {farTook} · 꺼짐 {Time.Clock(outAt)} · {string.Join(" / ", sm.Log.TakeLast(5).Select(l => l.text))}");
            Check("팔: 팔을 못 쓰는 로봇(0.6)은 곁의 불을 고르지 않고 주컴퓨터도 맡기지 않는다 — 멀리 있던 성한 로봇이 가서 끈다",
                !nearTook && farTook && outAt > 0,
                $"곁의 상한 로봇 불로 감 {nearTook} · 먼 성한 로봇 {farTook} · 불 {(outAt > 0 ? "꺼짐" : "안 꺼짐")}");
        }

        // ── 주컴퓨터 칩: 방 공기 65℃면 세상(⑥)의 열 보호가 끈다 — 원인 사슬이 그 방을 데운 원인까지 · 사실 '과열' · 주컴퓨터는 꺼져서 아무것도 못 하고 · 식으면 다시 켠다 ──
        {
            var s = Worlds.Full(lay, 7);
            var net = s.Get<Network>(); var f = s.Get<Field>(); var ch = s.Get<Chemistry>(); var sm = s.Get<ShipMind>();
            int comp = net.Devices.First(d => d.Type == "MainComputer").Id;
            int room = lay.Room[net.Devices[comp].Cell];
            int why = s.Causes.Add(CauseKind.Heat, "옆 방 공조가 멈춰 뜨거운 공기가 밀려들었다", s.Tick);
            void HoldAt(double k) { foreach (int c in lay.Rooms[room].Cells) { double gc = f.GasC(c); if (gc > 50) f.AddHeat(c, gc * (k - f.Temp(c))); ch.HeatCause[c] = why; } }
            s.Run((int)Time.Minutes(5));
            long cursor = s.Facts.Total;
            for (int k = 0; k < Time.Minutes(1); k++) { HoldAt(338); s.Step(); }
            bool tripped = net.ChipTrip[comp] != 0, offline = !sm.Online;
            string story = s.Causes.Story(net.ChipTripCause[comp]);
            var facts = s.Facts.Since(cursor).ToList();
            bool overheat = facts.Any(x => x.Kind == FactKind.Overheat && x.Other == comp);
            bool down = facts.Any(x => x.Kind == FactKind.ComputerDown && x.Cause == net.ChipTripCause[comp] && x.Cause != 0);
            for (int k = 0; k < Time.Minutes(2); k++) { HoldAt(295); s.Step(); }
            bool back = net.ChipTrip[comp] == 0 && sm.Online;
            if (Debug) Console.WriteLine($"    꺼짐 {tripped} · 주컴퓨터 멈춤 {offline} · 과열 사실 {overheat} · 멈춤 사실의 원인 {down} · 다시 켬 {back} · {story}");
            Check("주컴퓨터 칩: 방 공기 65℃면 세상의 열 보호가 끈다 (원인 사슬이 그 방을 데운 원인까지 · 사실 '과열') · 주컴퓨터는 멈춘다 (멈춤의 원인도 그 사슬) · 식으면 다시 켠다",
                tripped && offline && story.Contains("뜨거운 공기") && overheat && down && back,
                $"꺼짐 {tripped} · 멈춤 {offline} · 과열 {overheat} · 멈춤 원인 {down} · 다시 켬 {back} · \"{story}\"");
        }
    }
}
