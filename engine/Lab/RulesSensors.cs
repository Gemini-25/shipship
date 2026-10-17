using ShipEngine;

namespace Lab;

/// <summary>⑥ 망 — 센서 선: 방의 센서는 선을 타고 주컴퓨터에 닿는다 · 끊기면 그 방을 모른다 · 로봇이 끊긴 자리로 가서 잇는다. docs/DEPTH.md</summary>
public static partial class Program
{
    private static void SensorRules()
    {
        if (!Do("센서망")) return;
        Console.WriteLine("\n[⑥ 센서망]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);

        // ── 오른쪽 위 방의 센서 선이 미리 끊겨 있으면: 빈 방의 불을 주컴퓨터가 늦게 안다 — 먼저 '센서가 끊겼다'로 로봇을 끊긴 자리에 보내 잇고 나서야 경보 ──
        (long alarm, bool lost, long splices, string story) Run(bool cut)
        {
            var s = Worlds.Full(lay, 1);
            var m = s.Get<Mind>(); var ch = s.Get<Chemistry>(); var net = s.Get<Network>(); var sm = s.Get<ShipMind>();
            m.AddRobot(s, At(3, 7), AgentKind.Robot, At(3, 7));
            int room = lay.Room[At(15, 1)];
            var path = net.DataPath[room];
            int spot = path[path.Length / 2];
            s.Run(5);
            if (cut) net.CutData(s, spot, s.Causes.Add(CauseKind.Story, "낡은 센서 선이 바닥 밑에서 끊겼다", s.Tick, spot));
            long lit = s.Tick;
            ch.Ignite(At(15, 1), 3e6, s.Causes.Add(CauseKind.Story, "빈 방 의자 밑에서 불씨가 튀었다", s.Tick));
            long alarm = -1; bool lost = false;
            for (int k = 0; k < Time.Minutes(15) && alarm < 0; k++)
            {
                s.Step();
                lost |= sm.Incidents.Any(x => x.Kind == IncidentKind.SensorLost && x.Room == room);
                foreach (var f in s.Facts.Recent(20))
                    if (f.Kind == FactKind.ComputerAlarm && f.Other == (int)IncidentKind.Fire && alarm < 0) alarm = f.Tick - lit;
                if (Debug && cut && k % 25 == 0) Console.WriteLine($"    {k / 25}분 · 로봇 {m.Thought(m.Agents[0].Id)} 칸 {m.Agents[0].C.Cell} · 끊긴 자리 {spot} · 닿음 {net.RoomLinked(room)} · 불 {ch.BurningCount}");
            }
            var lostX = sm.Incidents.FirstOrDefault(x => x.Kind == IncidentKind.SensorLost && x.Room == room);
            return (alarm, lost, net.DataSplices, lost ? s.Causes.Story(lostX.Cause) : "");
        }
        var ok = Run(false); var cutRun = Run(true);
        Check("센서 선이 끊긴 방의 불은 주컴퓨터가 늦게 안다: 먼저 '센서가 끊겼다'로 로봇을 끊긴 자리에 보내 잇고 나서야 경보 (선이 멀쩡하면 바로)",
            ok.alarm >= 0 && ok.alarm < Time.Minutes(2) && cutRun.lost && cutRun.splices >= 1 && cutRun.alarm > ok.alarm + Time.Minutes(1) && cutRun.story.Contains("센서 선"),
            $"멀쩡: 경보 {ok.alarm / 25.0:0.0}분 / 끊김: 센서 끊김 사건 {cutRun.lost} · 이음 {cutRun.splices} · 경보 {(cutRun.alarm >= 0 ? $"{cutRun.alarm / 25.0:0.0}분" : "없음")} · \"{cutRun.story}\"");

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
            Check("불이 줄기선이 지나는 바닥을 250℃ 넘게 달구면 선이 녹아 끊긴다 — 그 너머 방들을 못 보고 · 원인 사슬은 그 불까지",
                net.DataCutAt(trunk) && blind >= 1 && s.Causes.Story(cutFact.Cause).Contains("포장재") && Enumerable.Range(0, lay.Rooms.Count).Where(r => !net.RoomLinked(r)).All(r => sm.RoomSeen[r] == 0),
                $"줄기 칸 {trunk} (방 {use[trunk]}개가 지난다) · 끊김 {net.DataCutAt(trunk)} · 못 보는 방 {blind} · \"{s.Causes.Story(cutFact.Cause)}\"");
        }
    }
}
