using ShipEngine;

namespace Lab;

/// <summary>⑩ 주컴퓨터 — 일지 · 아침 방송: 하루 한 번 주컴퓨터가 안 것만 적는다 (못 본 것은 못 적는다). docs/DEPTH.md</summary>
public static partial class Program
{
    private static void JournalRules()
    {
        if (!Do("일지")) return;
        Console.WriteLine("\n[⑩ 일지]");
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        int room = lay.Room[At(15, 1)];
        string name = lay.Rooms[room].Name;

        // 오른쪽 위 빈 방에 불 — 01시 일지 · 01시 아침 방송 (시험이라 한 시간 만에)
        // 못 보는 방은 긴 복도 배로 (방 다섯이 한 줄 · 닫힌 문): 주컴퓨터 방 문턱의 선이 끊기면 셋째 방부터는 무선도 닿지 않는다 (RulesSensors)
        var row = Worlds.Ascii(SensorRow, Worlds.MindLegend);
        int far = Worlds.At(row, 21, 1);
        string farName = row.Rooms[row.Room[far]].Name;
        (string journal, string morning, int count, int fromFire, int orphan) Run(bool cut)
        {
            var ly = cut ? row : lay;
            var s = Worlds.Full(ly, 1);
            var m = s.Get<Mind>(); var ch = s.Get<Chemistry>(); var net = s.Get<Network>(); var sm = s.Get<ShipMind>();
            sm.JournalHour = 1; sm.MorningHour = 1;
            if (!cut) m.AddRobot(s, At(3, 7), AgentKind.Robot, At(3, 7)); // 로봇이 없으면 끊긴 선을 아무도 잇지 않는다
            s.Run(5);
            if (cut) { int spot = Worlds.At(row, 6, 2); net.CutData(s, spot, s.Causes.Add(CauseKind.Setup, "낡은 센서 선이 문턱 밑에서 끊겼다", s.Tick, spot)); }
            int seed = s.Causes.Add(CauseKind.Setup, "빈 방 의자 밑에서 불씨가 튀었다", s.Tick);
            ch.Ignite(cut ? far : At(15, 1), 3e6, seed);
            s.Run((int)(Time.Hours(1) - s.Tick + 1));
            var fromFire = sm.Incidents.Where(x => x.Kind is IncidentKind.Fire or IncidentKind.CO or IncidentKind.Hot or IncidentKind.BadAir).ToList();
            if (Environment.GetEnvironmentVariable("RULE_DEBUG") == "1") foreach (var x in fromFire.Where(x => x.Cause == 0 || s.Causes.Root(x.Cause) != seed)) Console.WriteLine($"    첫 불씨에 안 닿는 사건: {Time.Clock(x.Since)} {x.Kind} {sm.RoomName(x.Room)} · {s.Causes.Story(x.Cause)}");
            return (sm.Journal.Count > 0 ? sm.Journal[^1].text : "", sm.Morning, sm.Journal.Count, fromFire.Count, fromFire.Count(x => x.Cause == 0 || s.Causes.Root(x.Cause) != seed));
        }
        var seen = Run(false); var blind = Run(true);
        Console.WriteLine($"    일지 (본 불):\n      {seen.journal.Replace("\n", "\n      ")}\n    아침 방송: {seen.morning}");
        Console.WriteLine($"    일지 (센서 선이 끊기고 무선도 닿지 않는 방의 불):\n      {blind.journal.Replace("\n", "\n      ")}");
        Check($"일지: 주컴퓨터가 본 불을 적는다 — 때 · 방 · 몇 분 만에 끝났나 · 로봇에게 맡긴 일 · 배 산소 · 아침 방송은 지난 일지 한 줄 + 오늘의 배",
            seen.count == 1 && seen.journal.Split('\n').Any(l => l.Contains(name) && l.Contains(": 불") && l.Contains("분 만에 끝났다")) && seen.journal.Contains("로봇에게 맡긴 일") && seen.journal.Contains("배: 산소")
            && seen.morning.StartsWith("좋은 아침입니다.") && seen.morning.Contains("지난 일지 — 사건") && seen.morning.Contains("오늘 배: 산소"),
            $"일지 {seen.count}장 · 첫 줄 \"{seen.journal.Split('\n')[0]}\"");
        Check("한 불에서 온 사건(그 방 · 옆 방의 연기 · 열기 · 일산화탄소 · 나쁜 공기)은 모두 원인 사슬이 첫 불씨까지 — 일지에는 한 사건으로",
            seen.fromFire >= 3 && seen.orphan == 0 && seen.journal.StartsWith("사건 1건"), $"불 쪽 사건 {seen.fromFire} · 첫 불씨에 안 닿는 것 {seen.orphan}");
        Check($"일지는 주컴퓨터가 안 것만: 센서 선이 끊기고 무선도 닿지 않는 방의 불은 못 적고 · '센서가 끊겼다' · '못 보는 방: … {farName}(센서 선 · 불이 급전선을 태웠으면 전원)' · 무선으로 닿는 옆 방은 따로",
            blind.count == 1 && !blind.journal.Split('\n').Any(l => l.Contains(farName) && l.Contains("불")) && blind.journal.Split('\n').Any(l => l.Contains(farName) && l.Contains("센서가 끊겼다"))
            && blind.journal.Contains("못 보는 방: ") && (blind.journal.Contains($"{farName}(센서 선)") || blind.journal.Contains($"{farName}(전원)")) && blind.journal.Contains("무선으로만 보는 방: "),   // 그 방 불이 급전선도 태우면 전원이 먼저 (⑥ 바퀴 3)
            $"첫 줄 \"{blind.journal.Split('\n')[0]}\"");

        // 오래 남은 연기: 불이 꺼지고 10분이 넘게 지나 문이 열려 연기가 옆 방(통로)으로 넘어가도 — 그 연기의 원인은 첫 불씨 (고친 결함: 원인이 비었다)
        {
            var s = Worlds.Full(lay, 1);
            var ch = s.Get<Chemistry>(); var sm = s.Get<ShipMind>(); var fld = s.Get<Field>();
            s.Run(5);
            int seed = s.Causes.Add(CauseKind.Setup, "빈 방 의자 밑에서 불씨가 튀었다", s.Tick);
            ch.Ignite(At(15, 1), 3e6, seed);
            s.Run((int)Time.Minutes(1));
            for (int c = 0; c < lay.N; c++) if (ch.IsBurning(c)) ch.Douse(c, Extinguishant.Water, 500);
            s.Run((int)Time.Minutes(12));
            bool out_ = Enumerable.Range(0, lay.N).All(c => !ch.IsBurning(c));
            int hall = lay.Room[At(5, 8)], door = lay.DoorAt[At(14, 6)];
            long opened = s.Tick;
            fld.SetDoor(door, 1);
            s.Run((int)Time.Minutes(2));
            var smoke = sm.Incidents.Where(x => x.Room == hall && x.Since >= opened && x.Kind is IncidentKind.Fire or IncidentKind.CO or IncidentKind.Hot or IncidentKind.BadAir).ToList();
            Check("오래 남은 연기: 불이 꺼지고 10분 넘게 지나 문이 열려 연기가 옆 방으로 넘어가도 그 사건의 원인은 첫 불씨 (원인이 비지 않는다)",
                out_ && smoke.Count > 0 && smoke.All(x => x.Cause != 0 && s.Causes.Root(x.Cause) == seed),
                $"꺼짐 {out_} · 통로 사건 {smoke.Count} · " + string.Join(" / ", smoke.Select(x => $"{x.Kind} {s.Causes.Story(x.Cause)}")));
        }
    }
}
