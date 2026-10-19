using ShipEngine;

namespace Lab;

/// <summary>⑪ 일과 물자 규칙 시험 — 일감판 · 만들기 · 예방 정비 · 청소 · 저절로 만드는 설비.</summary>
public static partial class Program
{
    private static void WorkRules()
    {
        if (!Do("일과물자")) return;
        Console.WriteLine("\n[일과 물자]");
        var lay = Worlds.Ascii(Worlds.MindShipComputer, Worlds.MindLegend);
        int At(int x, int y) => Worlds.At(lay, x, y);

        // ── 모자란 물건: 일감판이 로봇에게 맡기고 → 작업대에서 원료로 만든다 ──
        {
            var s = Worlds.Full(lay, 7);
            var m = s.Get<Mind>(); var w = s.Get<Work>();
            m.AddRobot(s, At(8, 7), AgentKind.Robot, At(8, 7));
            int bench = s.Layout.Furniture.FindIndex(f => f.Type == "Workbench");
            int ore = m.ItemIndex("원료"), patch = m.ItemIndex("봉합 꾸러미");
            m.Stock[bench * m.ItemCount + ore] = 4;
            int before = m.TotalOf(patch);
            s.Run(Time.Hours(3));
            Check("모자란 봉합 꾸러미: 일감판이 로봇에게 맡겨 작업대에서 원료로 만든다", m.TotalOf(patch) > before && m.TotalOf(ore) < 4 && w.Crafted > 0,
                $"봉합 꾸러미 {before} → {m.TotalOf(patch)} · 원료 4 → {m.TotalOf(ore)} · 만든 것 {w.Crafted}");
        }

        // ── 예방 정비: 표준 수명의 80%를 쓴 부품은 고장 나기 전에 간다 (숨은 한계는 모른다) ──
        {
            var s = Worlds.Full(lay, 7);
            var m = s.Get<Mind>(); var net = s.Get<Network>(); var w = s.Get<Work>();
            m.AddRobot(s, At(8, 7), AgentKind.Robot, At(8, 7));
            int dev = -1, part = -1;
            foreach (var d in net.Devices)
                for (int p = 0; p < 4 && dev < 0; p++)
                {
                    double life = d.Life((Part)p);
                    if (life <= 0) continue;
                    net.Wear[d.Id * 4 + p] = (float)(0.85 * life);
                    if (net.WearFraction(d.Id, (Part)p) < 0.95f) { dev = d.Id; part = p; }
                    else net.Wear[d.Id * 4 + p] = 0;
                }
            s.Run(Time.Hours(2));
            var fact = s.Facts.Since(0).Where(f => f.Kind == FactKind.Repaired).Select(f => s.Causes.Text(f.Cause)).FirstOrDefault(t => t.Contains("미리 갈았다")) ?? "";
            Check("예방 정비: 표준 수명의 80%를 쓴 부품은 고장 나기 전에 로봇이 미리 간다", dev >= 0 && net.Wear[dev * 4 + part] < 100 && net.Broken[dev * 4 + part] == 0 && w.Maintained > 0,
                dev < 0 ? "알맞은 부품이 없다" : $"{fact} · 닳음 {net.WearFraction(dev, (Part)part):0.00}");
        }

        // ── 곰팡이: 짙어지면 닦는다 ──
        {
            var s = Worlds.Full(lay, 7);
            var m = s.Get<Mind>(); var life = s.Get<Life>(); var w = s.Get<Work>();
            m.AddRobot(s, At(8, 7), AgentKind.Robot, At(8, 7));
            int c = At(17, 3);
            life.Mold[c] = 0.6f;
            s.Run(Time.Hours(1));
            Check("곰팡이가 짙어지면 일감판이 맡겨 닦아 낸다", life.Mold[c] < 0.05f && w.Cleaned > 0, $"곰팡이 0.6 → {life.Mold[c]:0.00} · 닦은 곳 {w.Cleaned}");
        }

        // ── 일감판은 주컴퓨터가 돌린다: 주컴퓨터가 멈추면 고인 물이 있어도 닦을 일을 올리지 않는다 (켜져 있으면 올린다) — 예전: 멈춰도 실제 값을 보고 올렸다 ──
        {
            (int jobs, bool online) Puddle(bool computerOk)
            {
                var s = Worlds.Full(lay, 7);
                var m = s.Get<Mind>(); var w = s.Get<Work>(); var f = s.Get<Field>(); var net = s.Get<Network>();
                m.AddRobot(s, At(8, 7), AgentKind.Robot, At(8, 7));
                if (!computerOk) net.Age(net.Devices.First(d => d.Type == "MainComputer").Id, Part.Electric, 1.0);
                s.Run(5);
                f.AddStore(At(17, 3), f.T.sWater, 40);
                s.Run(ShipMind.ChoreEvery * 2);
                return (s.Get<ShipMind>().Jobs.Count(j => j.Kind == GoalKind.Clean), s.Get<ShipMind>().Online);
            }
            var on = Puddle(true); var off = Puddle(false);
            Check("일감판은 주컴퓨터가 돌린다: 주컴퓨터가 멈추면 고인 물을 닦을 일을 올리지 않는다 (켜져 있으면 올린다)",
                on.jobs > 0 && on.online && off.jobs == 0 && !off.online, $"켜짐: 닦을 일 {on.jobs} · 멈춤: {off.jobs}");
        }

        // ── 정제기: 전기를 먹고 원료를 조금씩 만든다 ──
        {
            var s = ShipWorld.Create("Didim", 7);
            var m = s.Get<Mind>(); var w = s.Get<Work>();
            int ore = m.ItemIndex("원료");
            int before = m.TotalOf(ore);
            for (long t = 0; t < Time.Hours(5) && w.Made == 0; t++) s.Step(); // 처음 하나를 만들 때까지
            Check("정제기는 전기를 먹고 원료를 조금씩 만든다 (많아야 8)", w.Made > 0 && m.TotalOf(ore) > before, $"원료 {before} → {m.TotalOf(ore)} · 만든 것 {w.Made}");
        }
    }
}
