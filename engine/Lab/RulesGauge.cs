using ShipEngine;

namespace Lab;

/// <summary>판단 눈금 (docs/REVIEW.md ③-8): 판정이 흔들림을 알아보는가 · 로봇을 세는가 — 옛 눈금은 늘 0이었다.</summary>
public static partial class Program
{
    private static void GaugeRules()
    {
        if (!Do("판단 눈금")) return;
        Console.WriteLine("\n[판단 눈금]");
        (GoalKind, int, long) G(GoalKind g, int t, double min) => (g, t, Time.Minutes(min));
        bool flip = MindGauge.IsFlip(G(GoalKind.Wash, 84, 0), G(GoalKind.Wash, 85, 0.5), G(GoalKind.Wash, 84, 1), G(GoalKind.Wash, 85, 1.5));
        bool slow = MindGauge.IsFlip(G(GoalKind.Wash, 84, 0), G(GoalKind.Wash, 85, 2), G(GoalKind.Wash, 84, 4), G(GoalKind.Wash, 85, 6));
        bool idle = MindGauge.IsFlip(G(GoalKind.Idle, -1, 0), G(GoalKind.Eat, 3, 0.5), G(GoalKind.Idle, -1, 1), G(GoalKind.Eat, 3, 1.5));
        bool same = MindGauge.IsFlip(G(GoalKind.Eat, 3, 0), G(GoalKind.Eat, 3, 0.5), G(GoalKind.Eat, 3, 1), G(GoalKind.Eat, 3, 1.5));
        Check("흔들림: 3분 안에 변기 84 → 85 → 84 → 85는 흔들림 · 6분에 걸치면 · 쉼이 끼면 · 같은 것을 다시 세우면 아니다",
            flip && !slow && !idle && !same, $"빠름 {flip} · 느림 {slow} · 쉼 {idle} · 같음 {same}");

        var lay = Worlds.Ascii(WashCareShip, new Dictionary<char, string> { ['e'] = "Bed", ['r'] = "RobotDock", ['p'] = "PowerPanel", ['b'] = "Battery", ['c'] = "MainComputer",
            ['T'] = "Toilet", ['S'] = "Shower", ['k'] = "Sink", ['o'] = "Composter" });
        var s = Worlds.Full(lay, 5);
        var m = s.Get<Mind>();
        int dock = lay.Furniture.First(x => x.Type == "RobotDock").Cells[0];
        m.AddRobot(s, dock, AgentKind.Robot, dock);
        var g = new MindGauge(s);
        for (int i = 0; i < Time.Minutes(20); i++) { s.Step(); g.Observe(s.Tick); }
        Check("켜진 로봇을 센다 (놂 몫이 0~1)", g.RobotUp > 0 && g.RobotIdleShare is >= 0 and <= 1, g.Line());
        // 로봇 몫 (docs/MIND.md 4단): 소화기를 든 채 쉬는 로봇을 센다 — 돌려놓으면(10분 뒤) 더 늘지 않는다
        {
            var h = ShipWorld.Crewed("Hwasal", 11, 3, 3, 0);
            var hm = h.Get<Mind>();
            h.Run(Time.Minutes(10));
            foreach (var a in hm.Agents) if (a.C.Kind == AgentKind.Robot) { a.C.Holding = hm.iExt; a.C.HoldCharge = 500; }
            var hg = new MindGauge(h);
            for (int i = 0; i < Time.Minutes(40); i++) { h.Step(); hg.Observe(h.Tick); }
            long mid = hg.RobotHeldIdle;
            for (int i = 0; i < Time.Minutes(20); i++) { h.Step(); hg.Observe(h.Tick); }
            Check("로봇 몫: 소화기를 든 채 쉬는 로봇을 센다 · 돌려놓은 뒤엔 늘지 않는다 (든 채 논 시간 · 막힌 채 맡김 · 길 손해가 판정 줄에)",
                mid > 0 && hg.RobotHeldIdle == mid && hg.Line().Contains("든 채 논"), $"든 채 논 {mid * Time.SecondsPerTick / 60:0}분 → 그 뒤 20분 +{(hg.RobotHeldIdle - mid) * Time.SecondsPerTick / 60:0}분 · {hg.Line()}");
        }
    }
}
