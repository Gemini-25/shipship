using ShipEngine;

namespace Lab;

/// <summary>⑥ 바퀴 3 (나) 고장 원인 — 닳음의 까닭이 원인 마디로: 그을음 낀 거르개 · 정비 없이 돈 모터 · 뜨거운 데서 낡은 회로 · 오래 달궈진 전선 피복.</summary>
public static partial class Program
{
    // 방1 기계실(원자로 R · 배전반 p · 배터리 b · 정화기 s · 냉각 펌프 u · 주컴퓨터 c · 충전독 r) | 방2 휴게실(커피 기계 k)
    private const string WhyShip = @"
##################
#R..s..u..#......#
#p........D..k...#
#b..c..r..#......#
##################";

    private static void WhyRules()
    {
        if (!Do("고장 원인")) return;
        Console.WriteLine("\n[⑥ 고장 원인]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var legend = new Dictionary<char, string>
        {
            ['R'] = "ReactorCore", ['p'] = "PowerPanel", ['b'] = "Battery", ['s'] = "Scrubber", ['u'] = "CoolantPump", ['c'] = "MainComputer", ['r'] = "RobotDock", ['k'] = "CoffeeMachine",
        };
        var lay = Worlds.Ascii(WhyShip, legend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        (Sim s, Network net) World(bool robot = false)
        {
            var s = Worlds.Full(lay, 4);
            if (robot) s.Get<Mind>().AddRobot(s, At(7, 3), AgentKind.Robot, At(7, 3));
            s.Run(3);
            return (s, s.Get<Network>());
        }
        int Dev(Network n, string type) => n.Devices.First(d => d.Type == type).Id;
        string FailStory(Sim s, int dev, int part)
        {
            var f = s.Facts.Since(0).LastOrDefault(x => x.Kind == FactKind.MachineFail && x.Other == dev && x.Size == part);
            return f.Kind == FactKind.MachineFail ? s.Causes.Story(f.Cause) : "";
        }

        // ── 그을음 낀 거르개: 불이 지나간 방의 정화기 거르개는 빨리 막히고 · 망가지면 원인이 그 불까지 ──
        {
            (bool broke, string story, float wear) Run(bool fire)
            {
                var (s, net) = World();
                var f = s.Get<Field>(); var ch = s.Get<Chemistry>();
                int scrub = Dev(net, "Scrubber");
                if (fire)
                {
                    int spot = At(2, 2);
                    f.AddStore(spot, f.T.sPoly, 60);
                    ch.Ignite(spot, 3e6, s.Causes.Add(CauseKind.Setup, "기계실 바닥의 기름걸레에 불씨", s.Tick));
                    s.Run((int)Time.Minutes(6));
                }
                net.Neglect(s, scrub, Part.Filter, 900);
                return (net.IsBroken(scrub, Part.Filter), FailStory(s, scrub, (int)Part.Filter), net.WearFraction(scrub, Part.Filter));
            }
            var dirty = Run(true); var clean = Run(false);
            if (Debug) Console.WriteLine($"    {dirty.story}");
            Check("그을음 낀 거르개: 불이 지나간 방의 정화기 거르개는 같은 시간에 훨씬 빨리 막혀 망가지고 · 원인 사슬이 '그을음이 끼어' → 그 불의 불씨까지 (깨끗한 방은 아직 멀쩡)",
                dirty.broke && !clean.broke && dirty.story.Contains("그을음이 끼어") && dirty.story.Contains("기름걸레"),
                $"900시간 뒤 망가짐 {dirty.broke}/{clean.broke} · 깨끗한 방 마모 {clean.wear:0.00} · \"{dirty.story}\"");
        }

        // ── 정비 없이 돈 모터: 정비 때(수명의 80%)를 넘겨 돈 냉각 펌프 모터는 '베어링 기름이 말라' 망가진다 ──
        {
            var (s, net) = World();
            int pump = Dev(net, "CoolantPump");
            double life = net.Devices[pump].Life(Part.Mechanical);
            net.Neglect(s, pump, Part.Mechanical, life * 3);
            string story = FailStory(s, pump, (int)Part.Mechanical);
            Check("정비 없이 돈 모터: 갈아 끼울 때를 넘겨 돈 냉각 펌프 모터는 베어링 기름이 말라 망가진다 — 원인에 '정비 없이 n시간'",
                net.IsBroken(pump, Part.Mechanical) && story.Contains("베어링 기름이 말라") && story.Contains("정비 없이") && net.RunHours(pump, Part.Mechanical) > 0.8 * life,
                $"수명 {life:0}시간 · 돈 시간 {net.RunHours(pump, Part.Mechanical):0} · \"{story}\"");
        }

        // ── 뜨거운 데서 낡은 회로: 달궈진 칸의 주컴퓨터 회로는 일찍 낡고 · 원인이 달군 것까지 ──
        {
            var (s, net) = World();
            var f = s.Get<Field>(); var ch = s.Get<Chemistry>();
            int comp = Dev(net, "MainComputer");
            int cell = net.Devices[comp].Cell;
            ch.HeatCause[cell] = s.Causes.Add(CauseKind.Setup, "주컴퓨터 곁 배기구를 짐 상자가 막았다", s.Tick);
            f.Ts[cell] = 350;
            net.Neglect(s, comp, Part.Electric, 3000);
            string story = FailStory(s, comp, (int)Part.Electric);
            Check("뜨거운 데서 낡은 회로: 77℃로 달궈진 칸의 주컴퓨터 회로는 일찍 낡아 망가지고 · 원인 사슬이 '뜨거운 데서' → 달군 것(막힌 배기구)까지",
                net.IsBroken(comp, Part.Electric) && story.Contains("뜨거운 데서") && story.Contains("배기구"), $"\"{story}\"");
        }

        // ── 오래 달궈진 전선 피복: 늘 뜨거운 칸의 급전선 피복이 갈라지면 합선 → 차단기 → 로봇이 새 전선으로 잇는다 ──
        {
            var (s, net) = World(robot: true);
            var f = s.Get<Field>(); var ch = s.Get<Chemistry>();
            int lounge = lay.Room[At(13, 2)];
            var fd = net.Feeders[net.RoomFeeder[lounge]];
            int cell = fd.Path.Last(c => lay.Room[c] == lounge && c != net.Vent[lounge]);
            ch.HeatCause[cell] = s.Causes.Add(CauseKind.Setup, "휴게실 바닥 밑을 지나는 온수관 단열재가 벗겨졌다", s.Tick);
            net.AgeCable(cell, 0.9995);
            long tripAt = -1;
            for (int k = 0; k < (int)Time.Hours(2) && tripAt < 0; k++) { f.Tf[cell] = 400; s.Step(); if (net.Breaker[fd.Id] == 0) tripAt = s.Tick; }
            var trip = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.BreakerTrip && x.Other == fd.Id);
            string story = trip.Kind == FactKind.BreakerTrip ? s.Causes.Story(trip.Cause) : "";
            f.Tf[cell] = 293;
            for (int k = 0; k < (int)Time.Minutes(40) && net.FeederLineCut(fd.Id); k++) s.Step();
            s.Run((int)Time.Minutes(12));
            if (Debug) Console.WriteLine($"    {story}");
            Check("오래 달궈진 피복: 늘 뜨거운(127℃) 칸의 급전선 피복이 늙어 갈라지면 합선 → 차단기 (원인 사슬이 달군 것까지) → 로봇이 새 전선으로 잇고 다시 밝다",
                tripAt > 0 && story.Contains("피복이 갈라졌다") && story.Contains("온수관") && !net.FeederLineCut(fd.Id) && net.RoomPowered(lounge) && net.CableAge(cell) < 0.01,
                $"차단기 {(tripAt > 0 ? Time.Clock(tripAt) : "그대로")} · 이음 {!net.FeederLineCut(fd.Id)} · 밝음 {net.RoomPowered(lounge)} · \"{story}\"");
        }
    }
}
