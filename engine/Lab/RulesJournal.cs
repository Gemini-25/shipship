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
        (string journal, string morning, int count, int fromFire, int orphan) Run(bool cut)
        {
            var s = Worlds.Full(lay, 1);
            var m = s.Get<Mind>(); var ch = s.Get<Chemistry>(); var net = s.Get<Network>(); var sm = s.Get<ShipMind>();
            sm.JournalHour = 1; sm.MorningHour = 1;
            if (!cut) m.AddRobot(s, At(3, 7), AgentKind.Robot, At(3, 7)); // 로봇이 없으면 끊긴 선을 아무도 잇지 않는다
            s.Run(5);
            if (cut) { var path = net.DataPath[room]; int spot = path[path.Length / 2]; net.CutData(s, spot, s.Causes.Add(CauseKind.Story, "낡은 센서 선이 바닥 밑에서 끊겼다", s.Tick, spot)); }
            int seed = s.Causes.Add(CauseKind.Story, "빈 방 의자 밑에서 불씨가 튀었다", s.Tick);
            ch.Ignite(At(15, 1), 3e6, seed);
            s.Run((int)(Time.Hours(1) - s.Tick + 1));
            var fromFire = sm.Incidents.Where(x => x.Kind is IncidentKind.Fire or IncidentKind.CO or IncidentKind.Hot or IncidentKind.BadAir).ToList();
            return (sm.Journal.Count > 0 ? sm.Journal[^1].text : "", sm.Morning, sm.Journal.Count, fromFire.Count, fromFire.Count(x => x.Cause == 0 || s.Causes.Root(x.Cause) != seed));
        }
        var seen = Run(false); var blind = Run(true);
        Console.WriteLine($"    일지 (본 불):\n      {seen.journal.Replace("\n", "\n      ")}\n    아침 방송: {seen.morning}");
        Console.WriteLine($"    일지 (센서 선이 끊긴 방의 불):\n      {blind.journal.Replace("\n", "\n      ")}");
        Check($"일지: 주컴퓨터가 본 불을 적는다 — 때 · 방 · 몇 분 만에 끝났나 · 로봇에게 맡긴 일 · 배 산소 · 아침 방송은 지난 일지 한 줄 + 오늘의 배",
            seen.count == 1 && seen.journal.Split('\n').Any(l => l.Contains(name) && l.Contains(": 불") && l.Contains("분 만에 끝났다")) && seen.journal.Contains("로봇에게 맡긴 일") && seen.journal.Contains("배: 산소")
            && seen.morning.StartsWith("좋은 아침입니다.") && seen.morning.Contains("지난 일지 — 사건") && seen.morning.Contains("오늘 배: 산소"),
            $"일지 {seen.count}장 · 첫 줄 \"{seen.journal.Split('\n')[0]}\"");
        Check("한 불에서 온 사건(그 방 · 옆 방의 연기 · 열기 · 일산화탄소 · 나쁜 공기)은 모두 원인 사슬이 첫 불씨까지 — 일지에는 한 사건으로",
            seen.fromFire >= 3 && seen.orphan == 0 && seen.journal.StartsWith("사건 1건"), $"불 쪽 사건 {seen.fromFire} · 첫 불씨에 안 닿는 것 {seen.orphan}");
        Check($"일지는 주컴퓨터가 안 것만: 센서 선이 끊긴 방의 불은 못 적고 · '센서가 끊겼다' · '못 보는 방: {name}(센서 선)'",
            blind.count == 1 && !blind.journal.Split('\n').Any(l => l.Contains(name) && l.Contains("불")) && blind.journal.Contains($"{name}: 센서가 끊겼다") && blind.journal.Contains($"못 보는 방: {name}(센서 선)"),
            $"첫 줄 \"{blind.journal.Split('\n')[0]}\"");
    }
}
