using ShipEngine;

namespace Lab;

/// <summary>판단 결함 넷 (docs/REVIEW.md ③ — 함장 결정 '추천대로'): 사람 사건 열쇠 · 흔들림 · 손 비우기 · 일감 실패 기억.</summary>
public static partial class Program
{
    private static void MindFixRules()
    {
        if (!Do("판단 결함")) return;
        Console.WriteLine("\n[판단 결함 넷]");
        // ── 사람 사건은 사람에 묶는다: 덮지 않은 화상을 입은 이가 방을 오가도 사건은 하나 · 맡은 로봇이 바뀌지 않는다 (예전 20분에 사건 14개) ──
        {
            var s = ShipWorld.Crewed("Hwasal", 11, 2, 2, 0);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var sm = s.Get<ShipMind>();
            s.Run(Time.Minutes(10));
            var p = m.Agents.First(a => a.IsPerson);
            int body = p.C.Body;
            life[body].Burn = 0.4f;
            var rooms = s.Layout.Rooms.Where(r => r.Cells.Length > 6).ToArray();
            int hops = 0;
            for (long t = 1; t <= Time.Minutes(20); t++)
            {
                if (t % (long)Time.Minutes(1.5) == 0 && life[body].Dressed <= 0)   // 방을 오간다 (몸을 옮겨 흉내)
                {
                    var r = rooms[(hops * 3 + 1) % rooms.Length];
                    life[body].Cell = r.Cells[r.Cells.Length / 2];
                    hops++;
                }
                s.Step();
            }
            var ids = sm.Incidents.Where(x => x.Kind == IncidentKind.Injured && x.Subject == body).Select(x => x.Id).Distinct().ToList();
            Check("사람 사건은 사람에 묶인다: 화상 입은 이가 방을 오가도 '다친 사람' 사건은 하나 (예전엔 방마다 새로 서 20분에 14개)",
                ids.Count == 1 && hops >= 3, $"방 옮김 {hops}번 · 사건 {ids.Count}개 · 덮음 {life[body].Dressed:0.00}");
        }
        // ── 흔들림 (벽 너머 변기 둘): 디딤호 통신실의 마려운 이 — 벽 너머 화장실의 두 변기 사이를 오가지 않고 한 곳에서 볼일 (예전 2시간 반 오감) ──
        {
            var s = ShipWorld.Crewed("Didim", 11, 1, 0, 0);
            var m = s.Get<Mind>(); var life = s.Get<Life>();
            s.Run(5);
            var p = m.Agents.First(a => a.IsPerson);
            int spot = Worlds.At(s.Layout, 39, 15);
            p.C.Cell = spot; life[p.C.Body].Cell = spot; p.Plan.Clear(); p.Path.Clear(); p.PathIdx = 0;
            life[p.C.Body].Bladder = (float)(0.92 * Life.BladderMax); p.C.Sleepy = 0; p.C.Hunger = 0; p.C.Thirst = 0;
            var g = new MindGauge(s);
            long toilets0 = m.Toilets;
            for (int k = 0; k < Time.Minutes(30); k++) { s.Step(); g.Observe(s.Tick); p.C.Sleepy = 0; }
            Check("흔들림: 벽 너머 화장실의 변기 둘 사이를 오가지 않고 (길 거리로 고르고 · 고른 곳을 지킨다) 한 곳에서 볼일",
                g.Flips == 0 && m.Toilets > toilets0, $"흔들림 {g.Flips}번 · 볼일 {m.Toilets - toilets0} · {m.Thought(p.Id)}");
        }
        // ── 문턱 흔들림 (대피 중 목마른 이): 대피실에 든 이는 목이 말라도 문 앞에서 들락거리지 않고 대피실에 머문다 ──
        {
            var lay = Worlds.Ascii(Worlds.MindShip, Worlds.MindLegend);
            Sim? s = null; Mind? m = null; Agent? p = null;
            for (ulong seed = 1; seed < 40; seed++)
            {
                s = Worlds.Full(lay, seed); m = s.Get<Mind>();
                p = m[m.AddPerson(s, Worlds.At(lay, 15, 8))];
                s.Run(2);
                p.C.Diligent = 1;
                long h0 = m.SheltersHeeded;
                m.Shelter(s, lay.Room[Worlds.At(lay, 15, 2)], Worlds.At(lay, 15, 2), s.Tick + Time.Hours(2), 0, 1);
                if (m.SheltersHeeded > h0) break;
            }
            int shelter = lay.Room[Worlds.At(lay, 15, 2)];
            for (int k = 0; k < Time.Minutes(10); k++) s!.Step();
            bool arrived = m!.RoomOf(p!.C.Cell) == shelter;
            p.C.Thirst = 0.85f; p.C.Hunger = 0.3f;
            var g = new MindGauge(s!);
            int outside = 0;
            for (int k = 0; k < Time.Minutes(20); k++) { s!.Step(); g.Observe(s.Tick); if (m.RoomOf(p.C.Cell) != shelter) outside++; }
            Check("문턱 흔들림: 대피실에 든 이는 목이 말라도 문 앞에서 들락거리지 않고 대피실에 머문다 ('거기 있어라' 선택지가 방 안에서도 남는다)",
                arrived && g.Flips == 0 && outside < Time.Minutes(1), $"닿음 {arrived} · 흔들림 {g.Flips}번 · 대피실 밖 {outside * Time.SecondsPerTick / 60:0.0}분 · {m.Thought(p.Id)}");
        }
        // ── 길 거리 맡김: 불 곁 벽 너머 통로의 로봇(직선 3칸 · 돌아가면 15걸음)보다 같은 방 끝의 로봇(직선 8칸 · 10걸음)이 먼저 간다 ──
        var layC = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int AtC(int x, int y) => Worlds.At(layC, x, y);
        {
            var s = Worlds.Full(layC, 4);
            var m = s.Get<Mind>(); var ch = s.Get<Chemistry>(); var sm = s.Get<ShipMind>();
            int wallSide = m.AddRobot(s, AtC(20, 7), AgentKind.Robot, AtC(20, 7));   // 제자리에서 쉰다 (충전하러 떠나지 않게)
            int sameRoom = m.AddRobot(s, AtC(12, 2), AgentKind.Robot, AtC(12, 2));
            s.Run(5);
            ch.Ignite(AtC(20, 4), 3e6, s.Causes.Add(CauseKind.Setup, "의자 밑에서 불씨가 튀었다", s.Tick));
            int first = -1;
            for (int k = 0; k < Time.Minutes(3) && first < 0; k++)
            {
                s.Step();
                foreach (var x in sm.Incidents) if (x.Kind == IncidentKind.Fire && x.RobotA >= 0) { first = x.RobotA; break; }
            }
            Check("길 거리 맡김: 불에 먼저 보내는 로봇은 벽 너머 직선으로 가까운 로봇(돌아가면 15걸음)이 아니라 같은 방 끝의 로봇(10걸음)이다",
                first == sameRoom, $"먼저 간 로봇 {(first >= 0 ? m.NameOf(first) : "-")} (같은 방 {m.NameOf(sameRoom)} · 벽 너머 {m.NameOf(wallSide)})");
        }
        // ── 막힌 로봇 풀기: 불에 맡긴 로봇의 팔이 꺾이면 그 일에서 놓아주고 다른 로봇을 곧바로 보낸다 (예전엔 30분 동안 '맡은 이 있음') ──
        {
            var s = Worlds.Full(layC, 4);
            var m = s.Get<Mind>(); var ch = s.Get<Chemistry>(); var sm = s.Get<ShipMind>(); var n = s.Get<Network>();
            int ra = m.AddRobot(s, AtC(20, 7), AgentKind.Robot, AtC(20, 7));
            int rb = m.AddRobot(s, AtC(12, 2), AgentKind.Robot, AtC(12, 2));
            int rc = m.AddRobot(s, AtC(2, 8), AgentKind.Robot, AtC(2, 8));
            s.Run(5);
            ch.Ignite(AtC(20, 4), 3e6, s.Causes.Add(CauseKind.Setup, "의자 밑에서 불씨가 튀었다", s.Tick));
            for (int k = 0; k < Time.Minutes(3); k++) { s.Step(); if (sm.Incidents.Any(x => x.Kind == IncidentKind.Fire && (x.RobotA == rb || x.RobotB == rb))) break; }
            bool sent = sm.Incidents.Any(x => x.Kind == IncidentKind.Fire && (x.RobotA == rb || x.RobotB == rb));
            n.Robot(m[rb].C.Mech).Arms = 0.9f;   // 팔이 꺾였다
            long t0 = s.Tick, cAt = -1;
            for (int k = 0; k < Time.Minutes(5); k++)
            {
                s.Step();
                if (cAt < 0 && sm.Incidents.Any(x => x.Kind == IncidentKind.Fire && (x.RobotA == rc || x.RobotB == rc))) cAt = s.Tick - t0;
            }
            bool stillB = sm.Incidents.Any(x => x.Kind == IncidentKind.Fire && x.Active != 0 && (x.RobotA == rb || x.RobotB == rb));
            Check("막힌 로봇 풀기: 불에 맡긴 로봇의 팔이 꺾이면 그 일에서 놓아주고 남은 로봇을 곧바로 보낸다 (예전엔 30분 동안 맡은 이로 남았다)",
                sent && cAt >= 0 && cAt <= Time.Minutes(2) && !stillB, $"맡김 {sent} · 다른 로봇 {(cAt >= 0 ? $"{cAt * Time.SecondsPerTick:0}초 뒤" : "안 감")} · 꺾인 로봇 남음 {stillB}");
        }
        // ── 일감 결과 기억: 잠긴 방의 청소 — 못 한 로봇에게 다시 맡기지 않고 다른 로봇 → 모두 못 하면 '막힌 곳'으로 방송 (예전엔 같은 로봇에게 두 시간마다 · 디딤 사흘 24번) ──
        {
            var s = Worlds.Full(layC, 4);
            var m = s.Get<Mind>(); var sm = s.Get<ShipMind>(); var st = s.Get<Structure>(); var f = s.Get<Field>();
            m.AddRobot(s, AtC(20, 7), AgentKind.Robot, AtC(3, 7));
            m.AddRobot(s, AtC(22, 8), AgentKind.Robot, AtC(22, 8));
            s.Run(5);
            int dirty = AtC(2, 2);
            f.AddStore(dirty, s.Tables.sWater, 200);
            foreach (int door in new[] { AtC(10, 3), AtC(5, 6) }) st.LockDoor(s.Layout.DoorAt[door], s.Tick + Time.Hours(10));
            var given = new List<int>();
            for (int k = 0; k < Time.Minutes(90); k++)
            {
                s.Step();
                foreach (var j in sm.Jobs) if (j.Kind == GoalKind.Clean && j.Target == dirty && (given.Count == 0 || given[^1] != j.Agent)) given.Add(j.Agent);
            }
            bool again = given.Count != given.Distinct().Count();
            bool broadcast = sm.Blocks.Any(b => b.Contains("청소"));
            Check("일감 결과 기억: 잠긴 방의 청소를 못 한 로봇에게 다시 맡기지 않고 다른 로봇에게 → 모두 못 하면 '막힌 곳'으로 방송한다 (예전엔 같은 로봇에게 두 시간마다 되풀이)",
                given.Count >= 2 && !again && broadcast, $"맡은 차례 {string.Join(" → ", given.Select(a => m.NameOf(a)))} · 막힌 곳 [{string.Join(" | ", sm.Blocks.Where(b => b.Contains("청소")))}]");
        }
    }
}
