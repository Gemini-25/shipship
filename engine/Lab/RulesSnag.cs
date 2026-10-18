using ShipEngine;

namespace Lab;

/// <summary>⑧ · ⑩ 판단 엔진 — 막힘을 구조로 안다 (까닭 · 자리 · 대상) · 주컴퓨터가 맡긴 로봇의 막힘을 읽는다. docs/ENGINE.md 판단 엔진</summary>
public static partial class Program
{
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
    }
}
