using ShipEngine;

namespace Lab;

/// <summary>④ 열 · 진동 (깊이 바퀴 3) — 돌아가는 기계의 진동 · 볼트 풀림 · 덜컹 · 진동이 키운 금 · 볼트 조이기. docs/ENGINE.md ④ '열 · 진동'</summary>
public static partial class Program
{
    /// <summary>왼쪽 기관실(배전반 p · 냉각 펌프 U — 칸막이 벽 곁 · 배터리 b) | 문 D | 오른쪽 방(주컴퓨터 c · 배터리 b · 충전독 r).</summary>
    private const string VibShip = @"
###############
#p...U#c....r.#
#b....D.......#
#.....#.....b.#
###############";

    private static Dictionary<char, string> VibLegend => new()
    {
        ['p'] = "PowerPanel", ['U'] = "CoolantPump", ['b'] = "Battery", ['c'] = "MainComputer", ['r'] = "RobotDock",
    };

    /// <summary>다 풀리기 hours시간 전의 풀림 (지금 떨림으로 — 풀림 빠르기가 바뀌어도 시험의 때가 그대로).</summary>
    internal static float NearLoose(Network net, int d, double hours) => (float)(1 - hours / Network.LooseHours * Math.Pow(net.VibMPa(d) / Network.VibBaseMPa, 2));

    private static void HeatVibRules()
    {
        if (!Do("볼트")) return;
        Console.WriteLine("\n[④ 진동 — 볼트 풀림 · 덜컹 · 진동이 키운 금 · 조이기]");
        bool Debug = Environment.GetEnvironmentVariable("RULE_DEBUG") == "1";
        var lay = Worlds.Ascii(VibShip, VibLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);
        int wall = At(6, 1);

        (Sim s, Network net, int pump) Make(ulong seed, Action<Sim, Network, int>? setup = null)
        {
            var s = Worlds.Full(lay, seed);
            var net = s.Get<Network>();
            int pump = net.Devices.First(d => d.Type == "CoolantPump").Id;
            s.Run(25);   // 망이 한 번 돌아 풀림 장부를 연다
            setup?.Invoke(s, net, pump);
            return (s, net, pump);
        }

        // ── 풀림: 돌아가는 펌프의 볼트는 시간당 (세기 ÷ 4MPa)² ÷ 3,000만큼 풀린다 (DF10 — 예전 ÷ 200) · 다 풀리면 덜컹거린다 (사실 · 소리 · 원인) ──
        {
            var (s, net, pump) = Make(1);
            double a = net.VibMPa(pump), l0 = net.Loose[pump];
            s.Run(Time.Hours(2));
            double want = 2 / Network.LooseHours * Math.Pow(a / Network.VibBaseMPa, 2);
            double got = net.Loose[pump] - l0;
            net.Loose[pump] = NearLoose(net, pump, 0.2);   // 다 풀리기 12분 전
            s.Run(Time.Minutes(30));
            var fact = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Rattle && x.Other == pump);
            string story = fact.Kind == FactKind.Rattle ? s.Causes.Story(fact.Cause) : "";
            Check($"볼트 풀림: 돌아가는 냉각 펌프({a:0.0}MPa)의 볼트는 두 시간에 셈만큼 풀린다 (2% 안) · 다 풀리면 '덜컹거린다' (사실 · 원인)",
                net.Working(pump) && Rel(got, want) < 0.02 && net.Rattles == 1 && net.Loose[pump] >= 1 && story.Contains("볼트가 진동에 풀려 덜컹거린다"),
                $"풀림 +{got:0.0000} (셈 {want:0.0000}) · 덜컹 {net.Rattles} · \"{story}\"");
        }

        // ── 덜컹거리는 모터는 세 배 빨리 닳는다 ──
        {
            var (s1, n1, p1) = Make(2);
            var (s2, n2, p2) = Make(2, (s, net, pump) => net.Loose[pump] = 1);
            double w1 = n1.WearFraction(p1, Part.Mechanical), w2 = n2.WearFraction(p2, Part.Mechanical);
            s1.Run(Time.Hours(2)); s2.Run(Time.Hours(2));
            double d1 = n1.WearFraction(p1, Part.Mechanical) - w1, d2 = n2.WearFraction(p2, Part.Mechanical) - w2;
            Check("볼트가 풀려 덜컹거리는 펌프는 모터(기계 부품)가 세 배 빨리 닳는다 (3% 안)", d1 > 0 && Rel(d2 / d1, 3) < 0.03,
                $"보통 +{d1 * 100:0.000}% · 덜컹 +{d2 * 100:0.000}% · {d2 / Math.Max(1e-12, d1):0.00}배");
        }

        // ── 진동이 곁 벽의 금을 키운다: 새 펌프의 떨림은 문턱 아래 · 닳고 풀린 펌프는 칸막이의 5cm 금을 키운다 (원인: 볼트 풀림) ──
        {
            var (s1, n1, p1) = Make(3, (s, net, pump) => s.Get<Structure>().SeedCrack(wall, 0.05, 0));
            var (s2, n2, p2) = Make(3, (s, net, pump) => { s.Get<Structure>().SeedCrack(wall, 0.05, 0); net.Age(pump, Part.Mechanical, 0.7); net.Loose[pump] = 0.999f; });
            s1.Run(Time.Hours(1)); s2.Run(Time.Hours(1));
            var st1 = s1.Get<Structure>(); var st2 = s2.Get<Structure>();
            string story = s2.Causes.Story(st2.CrackCause[wall]);
            Check("진동: 새 펌프의 떨림으로는 곁 칸막이의 5cm 금이 그대로 · 닳고 볼트가 풀린 펌프 곁에서는 한 시간에 자란다 (원인: 볼트가 진동에 풀려)",
                st1.Crack[wall] == 0.05f && st1.VibGrowths == 0 && st2.Crack[wall] > 0.05f && st2.VibGrowths > 0 && story.Contains("볼트"),
                $"새 펌프 {n1.VibMPa(p1):0.0}MPa → {st1.Crack[wall] * 1000:0.00}mm · 낡고 풀린 펌프 {n2.VibMPa(p2):0.0}MPa → {st2.Crack[wall] * 1000:0.00}mm · \"{story}\"");
        }

        // ── 조이기: 주컴퓨터가 진동 센서로 덜컹거리는 장치를 알아채 로봇에게 맡긴다 — 로봇이 가서 5분 만에 조인다 ──
        {
            var (s, net, pump) = Make(4, (s, net, pump) =>
            {
                s.Get<Mind>().AddRobot(s, At(12, 1), AgentKind.Robot, At(12, 1));
                net.Loose[pump] = NearLoose(net, pump, 0.2);
            });
            long t0 = s.Tick;
            s.Run(Time.Minutes(40));
            var fact = s.Facts.Since(0).FirstOrDefault(x => x.Kind == FactKind.Repaired && x.Other == pump && x.Size == 10);
            string story = fact.Kind == FactKind.Repaired ? s.Causes.Story(fact.Cause) : "";
            if (Debug) Console.WriteLine("    " + string.Join("\n    ", s.Get<ShipMind>().Log.TakeLast(8).Select(x => $"{Time.Clock(x.tick)} {x.text}")));
            Check("조이기: 덜컹거리는 펌프를 주컴퓨터가 로봇에게 맡기고 · 로봇이 가서 볼트를 조인다 — 풀림이 걷힌다 (원인: 풀림 → 조였다)",
                net.Rattles == 1 && net.Tightened == 1 && net.Loose[pump] < 1 && story.Contains("덜컹") && story.Contains("볼트를 조였다"),
                $"덜컹 {net.Rattles} · 조임 {net.Tightened} · 풀림 {net.Loose[pump]:0.00} · {(fact.Kind == FactKind.Repaired ? $"{(fact.Tick - t0) / (double)Time.Minutes(1):0}분" : "-")} · \"{story}\"");
        }
    }
}
