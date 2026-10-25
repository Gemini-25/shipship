using ShipEngine;

namespace Lab;

/// <summary>평가 결함 (docs/MIND.md '지금 수준 — 객관 평가'가 찾은 판단 결함 다섯 — docs/NEXT.md 3단 덧붙임): 죽은 충전독 · 드론 맡김 · 놓아준 로봇 두 칸 · 산소 센서는 두 읽기로 · 폭풍의 끝은 선량계로.</summary>
public static partial class Program
{
    private static void AssessRules()
    {
        if (!Do("평가 결함")) return;
        Console.WriteLine("\n[평가 결함 — 죽은 독 · 드론 맡김 · 놓아준 칸 · 산소 센서 · 폭풍 끝]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        int[] docks = { At(3, 7), At(8, 5), At(22, 4) };
        int DockDev(Network net, int cell) => net.Devices.First(d => d.Furniture == lay.FurnitureAt[cell]).Id;

        // ── 죽은 독: 제 충전독의 전원이 내려져 있다 — 6분 동안 전지가 오르지 않으면 그 독을 피하고 가까운 다른 독에서 찬다 (예전엔 죽은 독에 붙은 채 전지가 줄었다) ──
        {
            var s = Worlds.Full(lay, 31);
            var m = s.Get<Mind>(); var net = s.Get<Network>();
            int r = m.AddRobot(s, docks[0], AgentKind.Robot, docks[0]);
            s.Run(12);
            net.SetOn(DockDev(net, docks[0]), false);
            net.Robot(m[r].C.Mech).Battery = 0.2f;
            long snags = m.Snags;
            for (int k = 0; k < Time.Minutes(60); k++)
            {
                s.Step();
                if (Debug && k % 50 == 0) Console.WriteLine($"    {k} 전지 {m[r].C.Battery:0.000} · {m.Thought(r)} 칸 {m[r].C.Cell} 목표 {m[r].C.Goal}:{m[r].C.GoalTarget}");
            }
            float bat = m[r].C.Battery;
            bool other = docks.Skip(1).Contains(m[r].C.Cell);
            Check("죽은 독: 제 충전독의 전원이 내려져 6분 동안 전지가 오르지 않으면 그 독을 피하고 가까운 다른 독에서 찬다 (예전엔 죽은 독에 붙은 채 전지가 줄었다)",
                bat > 0.35f && other && m.Snags > snags, $"한 시간 뒤 전지 {bat:0.00} (처음 0.20) · 자리 {(other ? "다른 독" : m[r].C.Cell == docks[0] ? "죽은 독" : "독 밖")} · 막힘 {m.Snags - snags}");
        }

        // ── 다 죽은 독: 독 셋이 모두 꺼졌다 — 독마다 한 번씩만 가 보고 (오락가락 없이) 독에서 기다리다 · 전원이 돌아오면 그 자리에서 찬다 ──
        {
            var s = Worlds.Full(lay, 32);
            var m = s.Get<Mind>(); var net = s.Get<Network>();
            int r = m.AddRobot(s, docks[0], AgentKind.Robot, docks[0]);
            s.Run(12);
            foreach (int d in docks) net.SetOn(DockDev(net, d), false);
            net.Robot(m[r].C.Mech).Battery = 0.25f;
            var went = new List<int>();
            for (int k = 0; k < Time.Hours(2); k++)
            {
                s.Step();
                if (m[r].C.Goal == GoalKind.Charge && (went.Count == 0 || went[^1] != m[r].C.GoalTarget)) went.Add(m[r].C.GoalTarget);
            }
            bool atDock = docks.Contains(m[r].C.Cell);
            float before = m[r].C.Battery;
            foreach (int d in docks) net.SetOn(DockDev(net, d), true);
            s.Run(Time.Minutes(30));
            float after = m[r].C.Battery;
            Check("다 죽은 독: 독 셋이 모두 꺼지면 독마다 한 번씩만 가 보고 (오락가락 없이) 독에서 기다리다 · 전원이 돌아오면 그 자리에서 찬다",
                went.Count <= 3 && went.Distinct().Count() == went.Count && atDock && after > before + 0.1f,
                $"간 독 차례 {string.Join(" → ", went.Select(c => Array.IndexOf(docks, c)))} · 기다린 자리 {(atDock ? "독" : "독 밖")} · 전원이 돌아온 뒤 전지 {before:0.00} → {after:0.00}");
        }

        // ── 드론 맡김: 로봇 없이 드론만 — 빈 방의 쾅을 주컴퓨터가 드론에게 맡기면 드론이 그 자리로 날아가 본다 (예전엔 드론에게 그 일의 선택지가 없어 30분 동안 쥐고만 있었다) ──
        {
            var s = Worlds.Full(lay, 33);
            var m = s.Get<Mind>(); var cm = s.Get<ShipMind>();
            int dr = m.AddRobot(s, At(3, 7), AgentKind.Drone, At(3, 7));
            s.Run(12);
            int src = At(17, 3);
            s.Get<Sound>().Emit(s, src, 85, SoundKind.Bang, s.Causes.Add(CauseKind.Setup, "빈 방 선반에서 공구 상자가 떨어졌다 (시험 차리기)", s.Tick));
            bool given = false, reached = false, closed = false;
            for (int k = 0; k < Time.Minutes(12) && !closed; k++)
            {
                s.Step();
                given |= cm.Incidents.Any(x => x.Kind == IncidentKind.Heard && (x.RobotA == dr || x.RobotB == dr));
                reached |= m.Dist(m[dr].C.Cell, src) <= 2;
                closed = given && !cm.Incidents.Any(x => x.Kind == IncidentKind.Heard && x.Active != 0);
                if (Debug && k % 20 == 0) Console.WriteLine($"    {k} 드론 {m.Thought(dr)} 칸 {m[dr].C.Cell} 맡김 {m[dr].C.AssignedGoal}:{m[dr].C.AssignedTarget} 거리 {m.Dist(m[dr].C.Cell, src)}");
            }
            Check("드론 맡김: 로봇 없이 드론만 있을 때 주컴퓨터가 쾅 소리의 자리를 드론에게 맡기면 드론이 날아가 보고 사건이 끝난다 (예전엔 맡김을 쥐고만 있었다)",
                given && reached && closed, $"맡김 {given} · 닿음 {reached} · 끝남 {closed}");
        }

        // ── 놓아준 로봇 두 칸: 잠긴 방의 쾅 — 막혀 놓아준 로봇에게 10분 안에는 다시 맡기지 않는다 (예전엔 칸이 하나라 A → B → A) ──
        {
            var s = Worlds.Full(lay, 34);
            var m = s.Get<Mind>(); var cm = s.Get<ShipMind>(); var st = s.Get<Structure>();
            int[] rbs = { m.AddRobot(s, At(2, 2), AgentKind.Robot, At(8, 5)), m.AddRobot(s, At(13, 7), AgentKind.Robot, At(3, 7)), m.AddRobot(s, At(21, 8), AgentKind.Robot, At(3, 7)) };
            s.Run(12);
            foreach (int door in new[] { At(10, 3), At(14, 6) }) st.LockDoor(s.Layout.DoorAt[door], s.Tick + Time.Hours(10));
            int src = At(17, 3);
            s.Get<Sound>().Emit(s, src, 90, SoundKind.Bang, s.Causes.Add(CauseKind.Setup, "잠긴 방 선반에서 공구 상자가 떨어졌다 (시험 차리기)", s.Tick));
            var sent = new List<(int rb, long at)>(); var dropped = new Dictionary<int, long>();
            long released0 = cm.Released;
            bool again = false;
            for (int k = 0; k < Time.Minutes(40); k++)
            {
                s.Step();
                foreach (var x in cm.Incidents)
                {
                    if (x.Kind != IncidentKind.Heard) continue;
                    foreach (int rb in new[] { x.RobotA, x.RobotB })
                    {
                        if (rb < 0 || sent.Any(v => v.rb == rb && v.at > dropped.GetValueOrDefault(rb, -1))) continue;
                        if (dropped.TryGetValue(rb, out long d) && s.Tick - d < Time.Minutes(10)) again = true;
                        sent.Add((rb, s.Tick));
                    }
                }
                foreach (var v in sent) if (!cm.Incidents.Any(x => x.Kind == IncidentKind.Heard && (x.RobotA == v.rb || x.RobotB == v.rb)) && dropped.GetValueOrDefault(v.rb, -1) < v.at) dropped[v.rb] = s.Tick;
                if (Debug && k % 50 == 0) Console.WriteLine($"    {k} 사건 {string.Join(",", cm.Incidents.Where(x => x.Active != 0).Select(x => $"{x.Kind}:{x.RobotA}/{x.RobotB}"))} · {string.Join(" / ", rbs.Select(rb => $"{m.NameOf(rb)} {m.Thought(rb)} 칸 {m[rb].C.Cell} 막힘 {m[rb].C.Snag} 맡김 {m[rb].C.AssignedGoal}~{m[rb].C.AssignedUntil - s.Tick}"))} · 들음 {cm.MicHeard}");
            }
            Check("놓아준 로봇 두 칸: 막혀 놓아준 로봇에게는 10분 안에 같은 일을 다시 맡기지 않는다 — 다른 로봇에게 (예전엔 칸이 하나라 A → B → A)",
                cm.Released - released0 >= 2 && !again, $"놓아줌 {cm.Released - released0} · 맡긴 차례 {string.Join(" → ", sent.Select(v => $"{m.NameOf(v.rb)}({Time.ToSeconds(v.at) / 60:0}분)"))} · 10분 안에 다시 {again}");
        }

        // ── 산소 센서는 두 읽기로: 조절 센서가 한 시간에 걸쳐 0.7까지 낮게 읽게 된다 — 주컴퓨터는 제 센서들의 평균과 조절 센서 값을 견줘 어긋나면 (방이 짙어지기 전에) 다시 맞춘다 · 바른 센서는 건드리지 않는다 ──
        {
            (long cal, double gain, float most, bool rich, string why) Run(bool drift)
            {
                var s = ShipWorld.Crewed("Hwasal", 35, 3);
                var net = s.Get<Network>(); var sm = s.Get<ShipMind>();
                s.Run(5);
                if (drift) net.StartO2Drift(1, s.Causes.Add(CauseKind.Setup, "시험: 조절 센서가 늙기 시작했다", s.Tick));
                float most = 0; long cal0 = net.O2Calibrations;
                for (int k = 0; k < Time.Hours(3); k++)
                {
                    s.Step();
                    if (k % 25 == 0) for (int r = 0; r < s.Layout.Rooms.Count; r++) if (sm.RoomSeen[r] != 0 && sm.RoomKPa[r] > 40) most = Math.Max(most, sm.RoomO2Pct[r]);
                }
                return (net.O2Calibrations - cal0, net.O2Gain, most, sm.Incidents.Any(x => x.Kind == IncidentKind.O2Rich), sm.Log.Select(l => l.Item2).FirstOrDefault(t => t.Contains("조절 센서")) ?? "");
            }
            var old = Run(true); var ok = Run(false);
            Check("산소 센서는 두 읽기로: 조절 센서가 낮게 읽기 시작하면 주컴퓨터가 제 센서들의 평균과 견줘 어긋남을 잡아 다시 맞춘다 — 방이 짙어지기(23.5%) 전에 · 바른 센서는 건드리지 않는다 (예전엔 방이 짙어진 뒤 숨은 고장값을 읽고서야)",
                old.cal >= 1 && old.gain > 0.99 && !old.rich && old.why.Contains("짙어지기 전에") && ok.cal == 0,
                $"늙은 센서: 다시 맞춤 {old.cal} · 센서 {old.gain:0.00} · 가장 짙은 방 {old.most:0.0}% · \"{old.why}\" / 바른 센서: 다시 맞춤 {ok.cal}");
        }

        // ── 폭풍 끝은 선량계로: 작은 양성자 폭풍 — 선량이 평소의 두 배 밑으로 내려가 3분이 지나면 푼다 (폭풍이 끝나는 때를 미리 알지 않는다 · 예전엔 숨은 끝 시각까지 기다렸다) ──
        {
            var wl = Worlds.Ascii(WeatherShip, new Dictionary<char, string> { ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer", ['e'] = "Bed", ['R'] = "SensorArray" });
            var s = Worlds.Full(wl, 36);
            var m = s.Get<Mind>(); var o = s.Get<Outside>(); var sm = s.Get<ShipMind>();
            m.AddPerson(s, Worlds.At(wl, 3, 1));
            s.Run(5);
            double calm = o.CalmDose, near = 1 / Math.Max(0.05, o.Distance * o.Distance);
            double scale = 3 * (calm + 0.05) / Math.Max(1e-9, o.StarDef.FlareDose * near);   // 꼭대기가 문턱의 세 배 — 폭풍 한가운데쯤 문턱 밑으로
            o.ScheduleFlare(s.Tick, 1, s.Causes.Add(CauseKind.Setup, "별의 표면이 끓어오르기 시작했다", s.Tick), scale);
            long low = -1, off = -1, end = -1; bool was = false, high = false;
            for (int k = 0; k < Time.Hours(12) && (off < 0 || end < 0); k++)
            {
                s.Step();
                was |= sm.ShelterOn == 2;
                if (o.FlareEnd > 0) end = o.FlareEnd;
                bool above = o.DoseOut > 2 * o.CalmDose + 0.05;
                high |= above;
                if (high && low < 0 && !above) low = s.Tick;   // 꼭대기를 지나 문턱 밑으로
                if (was && off < 0 && sm.ShelterOn == 0) off = s.Tick;
                if (o.FlareStart < 0 && off >= 0) break;
            }
            Check("폭풍 끝은 선량계로: 선량이 평소의 두 배 밑으로 내려가 3분이 지나면 푼다 — 폭풍이 끝나는 때를 미리 알지 않는다 (예전엔 숨은 끝 시각까지 기다렸다)",
                was && low > 0 && off >= low && off - low <= Time.Minutes(6) && end > 0 && off < end,
                $"대피 {was} · 선량이 문턱 밑으로 {(low > 0 ? $"{Time.ToSeconds(low) / 60:0}분" : "-")} · 풂 {(off > 0 ? $"{Time.ToSeconds(off) / 60:0}분" : "-")} · 숨은 끝 {(end > 0 ? $"{Time.ToSeconds(end) / 60:0}분" : "-")}");
        }
    }
}
