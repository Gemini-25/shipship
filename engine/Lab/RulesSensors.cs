using ShipEngine;

namespace Lab;

/// <summary>⑥ 망 — 센서 선 · 무선: 방의 센서는 선을 타고 주컴퓨터에 닿는다 · 끊기면 옆 방을 거쳐 무선으로 (느리게) · 그것도 안 닿으면 그 방을 모른다 · 로봇이 끊긴 자리로 가서 잇는다. docs/DEPTH.md</summary>
public static partial class Program
{
    /// <summary>방 다섯이 한 줄 (닫힌 보통 문) — 왼쪽 끝이 주컴퓨터 방.</summary>
    private const string SensorRow = @"
###############################
#..c..#.....#.....#..a..#.....#
#.p.b.D.....D.....D.....D.....#
#..r..#.....#.....#.....#.....#
###############################";

    private static void SensorRules()
    {
        if (!Do("센서망")) return;
        Console.WriteLine("\n[⑥ 센서망]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);

        // ── 불이 난 방까지 경보가 오는 시간 (cut: 그 방 센서 선을 미리 끊는다 · 나머지는 그 배에 맞춰) ──
        (long alarm, bool lost, long splices, string story, byte seen, int updates) Fire(Layout ly, int fireCell, int robotCell, Func<Sim, int> cutAt, Action<Sim>? setup = null)
        {
            var s = Worlds.Full(ly, 1);
            var m = s.Get<Mind>(); var ch = s.Get<Chemistry>(); var net = s.Get<Network>(); var sm = s.Get<ShipMind>();
            m.AddRobot(s, robotCell, AgentKind.Robot, robotCell);
            int room = ly.Room[fireCell];
            s.Run(5);
            setup?.Invoke(s);
            int spot = cutAt(s);
            if (spot >= 0) net.CutData(s, spot, s.Causes.Add(CauseKind.Story, "낡은 센서 선이 바닥 밑에서 끊겼다", s.Tick, spot));
            long lit = s.Tick;
            ch.Ignite(fireCell, 3e6, s.Causes.Add(CauseKind.Story, "빈 방 의자 밑에서 불씨가 튀었다", s.Tick));
            long alarm = -1; bool lost = false; byte seen = 0; int updates = 0; float last = sm.RoomKPa[room];
            for (int k = 0; k < Time.Minutes(15) && (alarm < 0 || k < Time.Minutes(3)); k++)
            {
                s.Step();
                if (k < Time.Minutes(3) && sm.RoomKPa[room] != last) { updates++; last = sm.RoomKPa[room]; }   // 불에 데워지는 방의 기압 읽기가 몇 번 바뀌나
                lost |= sm.Incidents.Any(x => x.Kind == IncidentKind.SensorLost && x.Room == room);
                seen = Math.Max(seen, sm.RoomSeen[room]);
                foreach (var f in s.Facts.Recent(20))
                    if (f.Kind == FactKind.ComputerAlarm && f.Other == (int)IncidentKind.Fire && alarm < 0) alarm = f.Tick - lit;
                if (Debug && spot >= 0 && k % 25 == 0) Console.WriteLine($"    {k / 25}분 · 로봇 {m.Thought(m.Agents[0].Id)} 칸 {m.Agents[0].C.Cell} · 끊긴 자리 {spot} · 닿음 {net.SensorLink(s.Tick, room)} · 불 {ch.BurningCount}");
            }
            var lostX = sm.Incidents.FirstOrDefault(x => x.Kind == IncidentKind.SensorLost && x.Room == room);
            return (alarm, lost, net.DataSplices, lost ? s.Causes.Story(lostX.Cause) : "", seen, updates);
        }

        // ── 선이 끊겨도 전기가 있으면 무선으로 본다: 옆 방(주컴퓨터 방)이 닫힌 문 하나 너머 — 경보가 조금 늦을 뿐 · '센서 선이 끊겼다'는 그대로 (선을 이을 일) ──
        {
            int fire = At(15, 1), room = lay.Room[fire];
            var ok = Fire(lay, fire, At(3, 7), _ => -1);
            var cut = Fire(lay, fire, At(3, 7), s => { var p = s.Get<Network>().DataPath[room]; return p[p.Length / 2]; });
            Check("센서 선이 끊겨도 전기가 있으면 무선으로 본다 (옆 방을 거쳐 · 읽기 세 번에 한 번) — 불 경보가 조금 늦을 뿐 · '센서 선이 끊겼다'는 그대로",
                ok.alarm >= 0 && ok.alarm < Time.Minutes(2) && cut.alarm >= ok.alarm && cut.alarm < ok.alarm + Time.Minutes(1) && cut.seen == 2 && cut.lost && cut.story.Contains("센서 선")
                && cut.updates * 2 < ok.updates && cut.updates >= 3,
                $"멀쩡: 경보 {ok.alarm / 25.0:0.0}분 · 3분 동안 기압 읽기가 바뀐 번 {ok.updates} / 끊김: 무선 {cut.seen == 2} · 바뀐 번 {cut.updates} · 경보 {(cut.alarm >= 0 ? $"{cut.alarm / 25.0:0.0}분" : "없음")} · 센서 끊김 사건 {cut.lost} · \"{cut.story}\"");
        }

        // ── 긴 복도 배 (방 다섯이 한 줄 · 닫힌 문): 주컴퓨터 방 문턱의 선이 끊기면 옆 방만 무선으로 닿고 (닫힌 문 2) 그 너머는 못 본다 (닫힌 문 둘 = 4 > 3)
        //    문을 열면 더 멀리 닿고 (열린 문 1) · 거치는 방의 전기가 끊기면 그 너머도 끊긴다 · 못 보는 방의 불은 로봇이 선을 이은 뒤에야 ──
        {
            var row = Worlds.Ascii(SensorRow, Worlds.MindLegend);
            int RAt(int x, int y) => Worlds.At(row, x, y);
            int[] rooms = { row.Room[RAt(3, 1)], row.Room[RAt(9, 2)], row.Room[RAt(15, 2)], row.Room[RAt(21, 2)], row.Room[RAt(27, 2)] };
            int sill = RAt(6, 2);
            var s = Worlds.Full(row, 1);
            var net = s.Get<Network>();
            s.Run(5);
            net.CutData(s, sill, s.Causes.Add(CauseKind.Story, "문턱 밑 센서 선이 끊겼다", s.Tick, sill));
            string Links() => string.Join("", rooms.Select(r => net.SensorLink(s.Tick, r)));
            s.Run(1); string shut = Links();
            int Door(int x) => row.Doors.First(d => d.Cell == RAt(x, 2)).Id;
            var st = s.Get<Structure>();
            st.RequestDoor(Door(6), 1); st.RequestDoor(Door(12), 1);
            s.Run(3); string open2 = Links();
            st.RequestDoor(Door(18), 1);
            s.Run(3); string open3 = Links();
            int feeder = net.RoomFeeder[rooms[1]];
            net.Breaker[feeder] = 0;
            s.Run(1); string dark = Links();
            var wired = Fire(row, RAt(21, 1), RAt(2, 3), _ => -1);
            var blind = Fire(row, RAt(21, 1), RAt(2, 3), _ => sill);
            Check("긴 복도 배: 문턱의 선이 끊기면 닫힌 문 하나 너머만 무선으로 닿고 그 너머는 못 본다 · 문을 열면 더 멀리 · 거치는 방이 꺼지면 끊긴다 · 못 보는 방의 불은 선을 이은 뒤에야 경보",
                shut == "12000" && open2 == "12200" && open3 == "12220" && dark[1] == '0' && dark[2] == '0' && dark[3] == '0'
                && wired.alarm >= 0 && wired.alarm < Time.Minutes(2) && blind.lost && blind.splices >= 1 && blind.alarm > wired.alarm + Time.Minutes(1) && blind.story.Contains("센서 선"),
                $"닿음(0 못 · 1 선 · 2 무선) 닫힘 {shut} · 문 둘 열림 {open2} · 셋 {open3} · 둘째 방 정전 {dark} / 불 경보: 선 {wired.alarm / 25.0:0.0}분 · 끊김 {(blind.alarm >= 0 ? $"{blind.alarm / 25.0:0.0}분" : "없음")} (이음 {blind.splices}) · \"{blind.story}\"");
        }

        // ── 불이 줄기선이 지나는 바닥을 250℃ 넘게 달구면 그 칸의 선이 녹아 끊긴다 — 그 너머 방들을 못 보고 · 원인 사슬은 그 불까지 ──
        {
            var s = Worlds.Full(lay, 2);
            var ch = s.Get<Chemistry>(); var net = s.Get<Network>(); var sm = s.Get<ShipMind>(); var f = s.Get<Field>();
            // 여러 방이 함께 쓰는 줄기 칸 (주컴퓨터 곁은 빼고)
            var use = new Dictionary<int, int>();
            foreach (var p in net.DataPath) foreach (int c in p.Skip(2)) use[c] = use.GetValueOrDefault(c) + 1;
            int trunk = use.Where(kv => lay.FurnitureAt[kv.Key] < 0).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
            s.Run(5);
            f.AddStore(trunk, f.T.sPoly, 40); // 바닥에 쌓인 짐 (불이 탈 것)
            ch.Ignite(trunk, 3e6, s.Causes.Add(CauseKind.Story, "통로에 쌓아 둔 포장재에 불이 붙었다", s.Tick, trunk));
            for (int k = 0; k < Time.Minutes(5) && !net.DataCutAt(trunk); k++) s.Step();
            s.Run(10);
            int blind = Enumerable.Range(0, lay.Rooms.Count).Count(r => !net.RoomLinked(r));
            var cutFact = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.SensorCut);
            Check("불이 줄기선이 지나는 바닥을 250℃ 넘게 달구면 선이 녹아 끊긴다 — 그 너머 방들은 선으로 못 본다 (무선이 닿는 방만 느리게) · 원인 사슬은 그 불까지",
                net.DataCutAt(trunk) && blind >= 1 && s.Causes.Story(cutFact.Cause).Contains("포장재") && Enumerable.Range(0, lay.Rooms.Count).Where(r => !net.RoomLinked(r)).All(r => sm.RoomSeen[r] != 1),
                $"줄기 칸 {trunk} (방 {use[trunk]}개가 지난다) · 끊김 {net.DataCutAt(trunk)} · 못 보는 방 {blind} · \"{s.Causes.Story(cutFact.Cause)}\"");
        }
    }
}
