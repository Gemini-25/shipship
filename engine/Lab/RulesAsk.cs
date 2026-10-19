using ShipEngine;

namespace Lab;

/// <summary>⑩ 주컴퓨터 (바퀴 2) — 사람에게 부탁 · 급한 차례 · 제 몸 · 이산화탄소 머무름. docs/DEPTH.md</summary>
public static partial class Program
{
    private static void AskRules()
    {
        if (!Do("부탁과 제 몸")) return;
        Console.WriteLine("\n[⑩ 부탁과 제 몸]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        int left = lay.Room[At(3, 2)], right = lay.Room[At(15, 1)];
        string Lines(Sim s, long from) => string.Join(" / ", s.Get<ShipMind>().Log.Where(l => l.tick >= from).Select(l => l.text));

        // ── 이산화탄소: 한 칸의 짙은 숨(사람 곁 한 모금)은 경보가 아니다 · 방 공기 전체가 5000ppm을 1분 넘게 넘으면 '공기가 나쁘다' 한 번 ──
        {
            (int badAir, long firstAfter) Run(bool whole)
            {
                var s = Worlds.Full(lay, 3);
                var f = s.Get<Field>(); var sm = s.Get<ShipMind>();
                s.Run(5);
                long t0 = s.Tick;
                var cells = lay.Rooms[left].Cells;
                if (whole) foreach (int c in cells) f.AddGas(c, f.T.gCO2, f.Moles(c) * 0.007, 293);   // 방 공기 전체 7000ppm 더
                else foreach (int c in cells.Take(2)) f.AddGas(c, f.T.gCO2, f.Moles(c) * 0.03, 310);       // 두 칸에만 짙은 날숨 (3만ppm — 옛 '가장 짙은 칸' 읽기면 경보)
                for (int k = 0; k < Time.Minutes(4); k++) s.Step();
                var bad = sm.Incidents.Where(x => x.Kind == IncidentKind.BadAir && x.Room == left).ToList();
                if (Debug) Console.WriteLine($"    {(whole ? "방 전체" : "한 칸 숨")}: 방 평균 {sm.RoomCO2[left]:0}ppm · 나쁜 공기 사건 {bad.Count}");
                return (bad.Count, bad.Count > 0 ? bad[0].Since - t0 : -1);
            }
            var puff = Run(false); var room = Run(true);
            Check("이산화탄소: 두 칸의 짙은 날숨(3만ppm)은 경보가 아니고 · 방 공기 전체가 5000ppm을 1분 넘게 넘으면 '공기가 나쁘다' 한 번 (방 평균 · 머무름)",
                puff.badAir == 0 && room.badAir == 1 && room.firstAfter >= Time.Minutes(1),
                $"한 칸 숨 사건 {puff.badAir} · 방 전체 사건 {room.badAir} ({Time.ToSeconds(room.firstAfter):0}초 뒤)");
        }

        // ── 로봇이 없으면 사람에게 부탁한다 — 1분 기다린 뒤 가깝고 솜씨 있는 이에게 · 받은 이가 불을 끄러 간다 / 로봇이 있으면 사람에게 묻지 않는다 ──
        {
            (long asked, long accepted, long firstAsk, bool went, string log) Run(bool robot, byte policy)
            {
                var s = Worlds.Full(lay, 4);
                var m = s.Get<Mind>(); var ch = s.Get<Chemistry>(); var sm = s.Get<ShipMind>();
                sm.Policy.AskPeople = policy;
                int p = m.AddPerson(s, At(12, 8));
                m.SetPersonality(p, 0.5f, 0.9f, 0.5f, 0.9f, 0.3f);
                m[p].C.SkFire = 0.8f;
                if (robot) m.AddRobot(s, At(3, 7), AgentKind.Robot, At(3, 7));
                s.Run(5);
                long t0 = s.Tick;
                ch.Ignite(At(15, 1), 3e6, s.Causes.Add(CauseKind.Story, "빈 방 의자 밑에서 불씨가 튀었다", s.Tick));
                long firstAsk = -1; bool went = false;
                for (int k = 0; k < Time.Minutes(5); k++)
                {
                    s.Step();
                    ref var c = ref m[p].C; c.Hunger = 0; c.Thirst = 0; c.Sleepy = 0;
                    if (firstAsk < 0 && sm.Asked > 0) firstAsk = s.Tick - t0;
                    if (c.Goal == GoalKind.FightFire) went = true;
                }
                if (Debug) Console.WriteLine($"    로봇 {robot} · 방침 {policy}: 부탁 {sm.Asked} 받음 {sm.AskAccepted} · {m.Thought(p)} · {Lines(s, t0)}");
                if (Debug) foreach (var fx in s.Facts.Since(0).Where(x => x.Kind is FactKind.Ignition or FactKind.FireSpread)) Console.WriteLine($"      {Time.Clock(fx.Tick)} {fx.Kind} ({lay.X(fx.Cell)},{lay.Y(fx.Cell)}) 방 {lay.Room[fx.Cell]} · {s.Causes.Story(fx.Cause)}");
                return (sm.Asked, sm.AskAccepted, firstAsk, went, Lines(s, t0));
            }
            var none = Run(false, 1); var withRobot = Run(true, 1);
            Check("로봇이 없으면 사람에게 부탁한다: 1분 기다린 뒤 가까운 이에게 '불을 꺼 주시겠어요?' — 받은 이가 불을 끄러 간다 · 로봇이 있으면 사람에게 묻지 않는다",
                none.asked >= 1 && none.accepted >= 1 && none.firstAsk >= Time.Minutes(1) && none.went && none.log.Contains("주시겠어요") && withRobot.asked == 0,
                $"로봇 없음: 부탁 {none.asked} · 받음 {none.accepted} · 첫 부탁 {Time.ToSeconds(none.firstAsk):0}초 뒤 · 끄러 감 {none.went} / 로봇 있음: 부탁 {withRobot.asked}");
            var off = Run(false, 0); var both = Run(true, 2);
            Check("함장 방침 '사람에게 맡기기': 끄면 로봇이 없어도 묻지 않고(스스로 나서는 것은 그 사람 몫) · '함께'면 로봇과 함께 곧바로 묻는다",
                off.asked == 0 && both.asked >= 1 && both.firstAsk >= 0 && both.firstAsk < Time.Minutes(1),
                $"끔: 부탁 {off.asked} / 함께: 부탁 {both.asked} · 첫 부탁 {Time.ToSeconds(both.firstAsk):0}초 뒤");
        }

        // ── 거절할 수 있다: 겁에 질린 이는 '무서워서' 거절 → 1분 뒤 다른 이에게 '급합니다'로 다시 — 그 사람이 받는다 ──
        {
            var s = Worlds.Full(lay, 5);
            var m = s.Get<Mind>(); var ch = s.Get<Chemistry>(); var sm = s.Get<ShipMind>();
            int scared = m.AddPerson(s, At(12, 8)), willing = m.AddPerson(s, At(3, 4));
            m.SetPersonality(scared, 0.5f, 0.05f, 0.5f, 0.05f, 0.9f);
            m.SetPersonality(willing, 0.5f, 0.9f, 0.5f, 0.9f, 0.3f);
            m[scared].C.SkFire = 0.8f; m[willing].C.SkFire = 0.6f;   // 겁먹은 이가 더 가깝고 솜씨도 낫다 — 주컴퓨터는 먼저 그에게 묻는다 (먼저 나섰다 물러난 이가 막 불을 만져 솜씨가 조금 올라도 차례가 뒤집히지 않게)
            m[scared].C.Brave = 0; m[willing].C.Brave = 0.8f;
            s.Run(5);
            long t0 = s.Tick;
            ch.Ignite(At(15, 1), 3e6, s.Causes.Add(CauseKind.Story, "빈 방 의자 밑에서 불씨가 튀었다", s.Tick));
            bool willingWent = false;
            for (int k = 0; k < Time.Minutes(5); k++)
            {
                s.Step();
                foreach (int p in new[] { scared, willing }) { ref var c = ref m[p].C; c.Hunger = 0; c.Thirst = 0; c.Sleepy = 0; }
                m[scared].C.Fear = Math.Max(m[scared].C.Fear, 0.8f);
                if (m[willing].C.Goal == GoalKind.FightFire) willingWent = true;
            }
            string log = Lines(s, t0);
            string sn = m.NameOf(scared), wn = m.NameOf(willing);
            if (Debug) Console.WriteLine($"    겁먹은 이 {sn} · 나설 이 {wn} · 부탁 {sm.Asked} 받음 {sm.AskAccepted} 거절 {sm.AskDeclined} · {log}");
            int askScared = log.IndexOf($"{sn}님"), askWilling = log.IndexOf($"{wn}님");
            Check("거절할 수 있다: 겁에 질린 이는 '무서워서 못 가겠어요' — 1분 뒤 다른 이에게 '로봇이 없습니다, 급합니다'로 다시 부탁하고 그 사람이 받아 불로 간다",
                sm.AskDeclined >= 1 && log.Contains($"{sn}: \"무서워서") && askScared >= 0 && askWilling > askScared && log.IndexOf("급합니다", askWilling) > 0 && willingWent,
                $"부탁 {sm.Asked} · 받음 {sm.AskAccepted} · 거절 {sm.AskDeclined} · 다른 이가 불로 감 {willingWent}");
        }

        // ── 급한 차례: 끼인 문을 바로잡으러 가던 하나뿐인 로봇이 불이 나면 불로 간다 (문은 미룬다) — 불을 끈 뒤 문으로 돌아간다 ──
        {
            var s = Worlds.Full(lay, 12);
            var m = s.Get<Mind>(); var ch = s.Get<Chemistry>(); var net = s.Get<Network>(); var sm = s.Get<ShipMind>();
            int r = m.AddRobot(s, At(3, 7), AgentKind.Robot, At(3, 7));
            s.Run(5);
            var st = s.Get<Structure>();
            int door = lay.DoorAt[At(5, 6)];
            st.JamDoor(s, door, s.Causes.Add(CauseKind.Story, "왼쪽 방 문이 틀어져 끼었다", s.Tick));
            for (int k = 0; k < Time.Minutes(1.5) && m[r].C.AssignedGoal != GoalKind.Brace; k++) s.Step();   // 로봇이 끼인 문을 맡을 때까지
            for (int k = 0; k < Time.Minutes(0.3); k++) s.Step();
            var before = m[r].C.AssignedGoal;
            long t0 = s.Tick;
            ch.Ignite(At(15, 1), 3e6, s.Causes.Add(CauseKind.Story, "빈 방 의자 밑에서 불씨가 튀었다", s.Tick));
            bool toFire = false, backToRepair = false;
            for (int k = 0; k < Time.Minutes(25); k++)
            {
                s.Step();
                if (m[r].C.AssignedGoal == GoalKind.FightFire && m[r].C.AssignedUntil > s.Tick) toFire = true;
                if (toFire && ch.BurningCount == 0 && m[r].C.AssignedGoal == GoalKind.Brace && m[r].C.AssignedUntil > s.Tick) backToRepair = true;
            }
            if (Debug) foreach (var fx in s.Facts.Since(0).Where(fx => fx.Kind is FactKind.Ignition or FactKind.MachineFail or FactKind.BreakerTrip)) Console.WriteLine($"      {Time.ToSeconds(fx.Tick - t0):0}초 {fx.Kind} 칸 {fx.Cell} 방 {lay.Room[fx.Cell]} · {s.Causes.Story(fx.Cause)}");
            if (Debug) Console.WriteLine($"    처음 맡은 일 {before} · 불로 {toFire} · 돌아감 {backToRepair} · 돌림 {sm.Preempts} · {m.Thought(r)} · {Lines(s, t0)}");
            Check("급한 차례: 끼인 문을 바로잡으러 가던 하나뿐인 로봇이 불이 나면 불로 간다 ('더 급하다') — 불을 끈 뒤 미뤄 둔 문으로 돌아간다",
                before == GoalKind.Brace && toFire && sm.Preempts >= 1 && backToRepair,
                $"처음 맡은 일 {before} · 불로 감 {toFire} · 돌린 번 {sm.Preempts} · 문으로 돌아감 {backToRepair}");
        }

        // ── 제 몸: 평소(제 열만)에는 느려지지 않는다 · 방 공기 45℃면 느려지고 · 65℃면 멈춘다(그 사이 난 불은 모른다) · 식으면 다시 켜고 그 불을 찾는다 ──
        {
            var s = Worlds.Full(lay, 7);
            var m = s.Get<Mind>(); var ch = s.Get<Chemistry>(); var f = s.Get<Field>(); var sm = s.Get<ShipMind>();
            m.AddRobot(s, At(3, 7), AgentKind.Robot, At(3, 7));
            int comp = lay.Room[At(7, 1)];
            void HoldAt(double k) { foreach (int c in lay.Rooms[comp].Cells) { double gc = f.GasC(c); if (gc > 50) f.AddHeat(c, gc * (k - f.Temp(c))); } }
            s.Run((int)Time.Minutes(30));
            long calmThrottles = sm.Throttles;
            for (int k = 0; k < Time.Minutes(1); k++) { HoldAt(318); s.Step(); }
            bool slow = sm.Throttled != 0 && sm.Online;
            for (int k = 0; k < Time.Minutes(1); k++) { HoldAt(338); s.Step(); }
            bool stopped = !sm.Online && sm.HeatStopped != 0;
            ch.Ignite(At(15, 1), 3e6, s.Causes.Add(CauseKind.Story, "빈 방 의자 밑에서 불씨가 튀었다", s.Tick));
            for (int k = 0; k < Time.Minutes(1); k++) { HoldAt(338); s.Step(); }
            bool blindToFire = !sm.Incidents.Any(x => x.Kind == IncidentKind.Fire);
            long cooled = s.Tick; long seenAt = -1;
            for (int k = 0; k < Time.Minutes(3) && seenAt < 0; k++) { HoldAt(295); s.Step(); if (sm.Incidents.Any(x => x.Kind == IncidentKind.Fire)) seenAt = s.Tick - cooled; }
            if (Debug) Console.WriteLine($"    평소 느려짐 {calmThrottles} · 45℃ 느려짐 {slow} · 65℃ 멈춤 {stopped} · 불 모름 {blindToFire} · 식힌 뒤 {Time.ToSeconds(seenAt):0}초 · {string.Join(" / ", sm.Log.TakeLast(8).Select(l => l.text))}");
            Check("제 몸: 평소(제 열만)엔 느려지지 않고 · 주컴퓨터 방 공기 45℃면 느려지고 · 65℃면 멈춘다 (그 사이 난 불은 모른다) · 식으면 다시 켜고 그 불을 찾는다",
                calmThrottles == 0 && slow && stopped && blindToFire && seenAt >= 0 && sm.HeatStops == 1,
                $"평소 느려짐 {calmThrottles} · 느려짐 {slow} · 멈춤 {stopped} · 불을 모름 {blindToFire} · 식힌 뒤 불을 앎 {(seenAt >= 0 ? $"{Time.ToSeconds(seenAt):0}초" : "못 함")}");
        }

        // ── 몰림: 사건이 다섯을 넘으면 앞일(복제 세계)을 그려 보지 않고 방침대로 — 몰리지 않았으면 사람이 있는 불 방을 닫기 전에 앞일을 그려 본다 ──
        {
            (long overloads, int imagined) Run(bool many)
            {
                var s = Worlds.Full(lay, 8);
                var m = s.Get<Mind>(); var ch = s.Get<Chemistry>(); var net = s.Get<Network>(); var sm = s.Get<ShipMind>(); var life = s.Get<Life>();
                int hurt = m.AddPerson(s, At(18, 4));
                void Pinned() => life[m[hurt].C.Body].Pain = 1;   // 다리를 다쳐 불 방에서 못 나가는 사람 (아픔 1 — 쓰러져 있다): '사람이 있는 불 방'
                Pinned(); s.Run(5);
                if (many)
                {
                    foreach (var d in net.Devices.Where(d => d.Type == "RobotDock")) net.Age(d.Id, Part.Electric, 1.0);   // 충전대 셋이 망가지고
                    foreach (int room in new[] { left, lay.Room[At(12, 8)] }) { var path = net.DataPath[room]; net.CutData(s, path[path.Length / 2], s.Causes.Add(CauseKind.Story, "센서 선이 끊겼다", s.Tick)); } // 센서 선 둘이 끊겼다
                }
                for (int k = 0; k < Time.Minutes(1); k++) { Pinned(); s.Step(); }
                ch.Ignite(At(15, 1), 3e6, s.Causes.Add(CauseKind.Story, "빈 방 의자 밑에서 불씨가 튀었다", s.Tick));
                for (int k = 0; k < Time.Minutes(3); k++) { Pinned(); s.Step(); }
                if (Debug) Console.WriteLine($"    {(many ? "고장 셋 · 끊긴 선 둘 + 불" : "불만")}: 사건 {string.Join(",", sm.Incidents.Where(x => x.Active != 0).Select(x => x.Kind))} · 몰림 {sm.Overloads} · 그려 봄 {sm.Imaginations.Count} · {string.Join(" / ", sm.Log.Select(l => l.text).Where(t => t.Contains("몰렸") || t.Contains("5분 뒤")))}");
                return (sm.Overloads, sm.Imaginations.Count);
            }
            var calm = Run(false); var busy = Run(true);
            Check("몰림: 사건이 다섯을 넘으면 '사건이 몰렸다' — 앞일(복제 세계)을 그려 보지 않고 방침대로 · 몰리지 않았으면 사람이 있는 불 방을 닫기 전에 앞일을 그려 본다",
                busy.overloads >= 1 && busy.imagined == 0 && calm.overloads == 0 && calm.imagined > 0,
                $"몰림: {busy.overloads}번 · 그려 봄 {busy.imagined} / 불만: 그려 봄 {calm.imagined}");
        }
    }
}
