using ShipEngine;

namespace Lab;

/// <summary>
/// 꾸밈의 센서 셋 (함장 승인 — docs/FURNISH.md): 누출 센서 · 진동 센서 · 열화상 카메라가 주컴퓨터의 눈이 된다 — 그 방의 샘 · 풀림 · 뜨거운 겉면을 더 일찍 안다.
/// 같은 상황을 센서가 돌 때 / 꺼졌을 때로 견준다.
/// </summary>
public static partial class Program
{
    // 방1 원자로실(원자로 R · 냉각 펌프 u · 진동 센서 V · 열화상 T · 배터리 b) | 방2 주컴퓨터실(배전반 p · 주컴퓨터 c · 충전독 r) | 방3 생명유지실 | 방4 휴게실(커피 기계 k · 누출 센서 L)
    private const string EyeShip = @"
#################################
#R....u.V#.....#....O...W.#....L#
#........D..p..D..........D..k..#
#b.....T.#..c.r#..v....g..#.....#
#################################";

    private static void EyeRules()
    {
        if (!Do("센서 눈")) return;
        Console.WriteLine("\n[꾸밈의 센서 셋 — 누출 · 진동 · 열화상]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var legend = new Dictionary<char, string>
        {
            ['R'] = "ReactorCore", ['u'] = "CoolantPump", ['v'] = "CoolantPump", ['b'] = "Battery", ['p'] = "PowerPanel", ['c'] = "MainComputer", ['r'] = "RobotDock",
            ['O'] = "OxygenGenerator", ['W'] = "WaterRecycler", ['g'] = "GrowBed", ['k'] = "CoffeeMachine",
            ['V'] = "VibrationMonitor", ['T'] = "ThermalCamera", ['L'] = "LeakDetector",
        };
        var lay = Worlds.Ascii(EyeShip, legend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        int room1 = lay.Room[At(3, 2)], room4 = lay.Room[At(29, 2)];

        (Sim s, Network net) World(string? off, bool robot)
        {
            var s = Worlds.Full(lay, 5);
            if (robot) s.Get<Mind>().AddRobot(s, At(14, 3), AgentKind.Robot, At(14, 3));
            s.Run(3);
            var net = s.Get<Network>();
            net.SetOn(net.Devices.First(d => d.Type == "CoffeeMachine").Id, true);
            if (off != null) net.SetOn(net.Devices.First(d => d.Type == off).Id, false);   // 센서를 끈다 (견줄 세계)
            s.Run(2);
            return (s, net);
        }
        string Log(Sim s, long from) => string.Join(" / ", s.Get<ShipMind>().Log.Where(x => x.tick >= from).Select(x => x.text));

        // ── 진동 센서: 볼트가 반쯤 풀린 냉각 펌프 — 센서가 돌면 덜컹거리기 전에 로봇이 조인다 · 꺼져 있으면 아무도 모른다 (덜컹거릴 때까지) ──
        {
            (long tightened, long rattles, string story) Run(string? off)
            {
                var (s, net) = World(off, robot: true);
                int pump = net.Devices.First(d => d.Type == "CoolantPump" && lay.Room[d.Cell] == room1).Id;
                net.Loose[pump] = 0.7f;
                s.Run(Time.Minutes(40));
                var fact = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Repaired && x.Other == pump && x.Size == 10);
                return (net.Tightened, net.Rattles, fact.Kind == FactKind.Repaired ? s.Causes.Story(fact.Cause) : "");
            }
            var on = Run(null); var dark = Run("VibrationMonitor");
            Check("진동 센서: 볼트가 70% 풀린 냉각 펌프 — 센서가 돌면 덜컹거리기 전에 로봇이 가서 조인다 ('풀려 가던') · 센서를 끄면 40분 동안 아무도 모른다",
                on.tightened == 1 && on.rattles == 0 && on.story.Contains("풀려 가던") && dark.tightened == 0,
                $"센서 켬: 조임 {on.tightened} · 덜컹 {on.rattles} · \"{on.story}\" / 센서 끔: 조임 {dark.tightened}");
        }

        // ── 누출 센서: 휴게실 물관의 바늘구멍 — 센서가 돌면 밸브로 좁히지 않고 바로 그 방으로 로봇을 보낸다 · 꺼져 있으면 밸브를 잠가 가며 좁힌다 ──
        {
            (long fixedMin, long valveTests, string log) Run(string? off)
            {
                var (s, net) = World(off, robot: true);
                var pipe = Enumerable.Range(0, lay.N).Where(c => (net.LineAt[c] & Network.Bit(LineKind.Water)) != 0 && lay.Room[c] == room4).OrderBy(c => c).ToList();
                int hole = pipe[pipe.Count / 2];
                net.AgeRust(hole, LineKind.Water, 1.0, s.Causes.Add(CauseKind.Setup, "휴게실 물관이 녹슬었다", s.Tick));
                long t0 = s.Tick, fixedAt = -1, tests0 = s.Get<ShipMind>().ValveTests;
                for (int k = 0; k < (int)Time.Hours(1) && fixedAt < 0; k++) { s.Step(); if (net.LeakAt(hole, LineKind.Water) <= 0 && s.Tick - t0 > 100) fixedAt = s.Tick; }
                return (fixedAt > 0 ? (fixedAt - t0) / Time.Minutes(1) : -1, s.Get<ShipMind>().ValveTests - tests0, Log(s, t0));
            }
            var on = Run(null); var dark = Run("LeakDetector");
            if (Debug) Console.WriteLine($"    켬: {on.log}\n    끔: {dark.log}");
            Check("누출 센서: 휴게실 물관의 바늘구멍 — 센서가 돌면 밸브를 잠가 보지 않고 바로 그 방으로 로봇을 보내 죈다 · 센서를 끄면 밸브를 잠가 가며 좁힌다 (더 늦게)",
                on.fixedMin >= 0 && on.valveTests == 0 && on.log.Contains("누출 센서에 물기가 잡혔다") && dark.fixedMin >= 0 && dark.valveTests > 0 && on.fixedMin <= dark.fixedMin,
                $"센서 켬: 죈 때 {on.fixedMin}분 · 밸브 시험 {on.valveTests} / 센서 끔: 죈 때 {dark.fixedMin}분 · 밸브 시험 {dark.valveTests}");
        }

        // ── 열화상 카메라: 원자로실 배터리 셀이 67℃로 달아올랐다 (방 공기는 그대로) — 카메라가 돌면 주컴퓨터가 알고 로봇을 보낸다 · 꺼져 있으면 모른다 ──
        {
            (bool seen, float airK, string log) Run(string? off)
            {
                var (s, net) = World(off, robot: true);
                var bat = net.Devices.First(d => d.Type == "Battery" && lay.Room[d.Cell] == room1);
                for (int k = 0; k < 2; k++) net.CellK[bat.Id * Network.CellGroups + k] = 340;
                long t0 = s.Tick;
                s.Run(Time.Minutes(2));
                var sm = s.Get<ShipMind>();
                bool seen = sm.Incidents.Any(x => x.Kind == IncidentKind.Hot && x.Room == room1 && x.Subject == 1 && x.Cell == bat.Cell);
                return (seen, sm.RoomAirT[room1], Log(s, t0));
            }
            var on = Run(null); var dark = Run("ThermalCamera");
            Check("열화상 카메라: 원자로실 배터리 셀 묶음이 67℃ (방 공기는 그대로) — 카메라가 돌면 주컴퓨터가 그 자리를 알고 · 꺼져 있으면 모른다",
                on.seen && on.log.Contains("열화상") && on.airK < 318 && !dark.seen,
                $"카메라 켬: 알았다 {on.seen} · 방 공기 {on.airK - 273.15:0}℃ · \"{on.log}\" / 카메라 끔: 알았다 {dark.seen}");
        }

        // ── 열화상의 헛경보: 평시 꾸민 배 한 시간 — 원자로 · 엔진 겉면이 늘 뜨거워도 열화상 경보는 없다 ──
        {
            var counts = new List<string>(); int alarms = 0;
            foreach (var key in new[] { "Didim", "Salpim", "Saetteul" })
            {
                var s = ShipWorld.Crewed(key, 4, 2, 1);
                s.Run(Time.Hours(1));
                int n = s.Get<ShipMind>().Incidents.Count(x => x.Kind == IncidentKind.Hot && x.Subject == 1);
                int cams = s.Get<Network>().Devices.Count(d => d.Type == "ThermalCamera");
                alarms += n; counts.Add($"{key} 카메라 {cams} · 경보 {n}");
            }
            Check("열화상의 헛경보: 평시 꾸민 배 (디딤 · 살핌 · 새뜰) 한 시간 — 늘 뜨거운 설비가 있어도 열화상 경보는 없다", alarms == 0, string.Join(" · ", counts));
        }
    }
}
